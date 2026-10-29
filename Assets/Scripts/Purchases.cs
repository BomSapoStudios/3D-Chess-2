using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Buying Customize items with money through the App Store (Unity IAP 5, StoreKit 2). Every item
/// that isn't free (Store.PaidIds), and Unlock Everything, is a non-consumable product named Store.ProductId(id), so new
/// items are sold as soon as they're in the enums and in App Store Connect. A purchase or restore
/// is the player's at once, and its signed transaction goes to the game server (Wallet), which checks
/// it with Apple's certificates and keeps the item with the account. The App Store is told it's done
/// only once the server has it, so one interrupted (or offline) comes back on the next launch.
/// Starts by itself; on the web there's no App Store and only pawns work.
/// </summary>
public class Purchases : MonoBehaviour
{
    public enum Outcome { Bought, Cancelled, Pending, Failed, Unavailable }

    /// <summary>Prices arrived from the App Store (labels can show the local price)</summary>
    public static event Action PricesLoaded;
    /// <summary>An item became owned through the App Store (bought, restored or re-delivered)</summary>
    public static event Action<string> Owned;
    /// <summary>An item arrived without a Buy tap or a restore: an offer code redeemed (here or in the App Store)</summary>
    public static event Action<string> Redeemed;

    private bool restoring;

    private static Purchases instance;
    private StoreController store;
    private bool connected, productsReady;
    private readonly Dictionary<string, string> idOfProduct = new Dictionary<string, string>();
    private readonly Dictionary<string, Action<Outcome>> waiting = new Dictionary<string, Action<Outcome>>();

    /// <summary>Products are loaded and can be bought</summary>
    public static bool Ready => instance != null && instance.productsReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("Purchases");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Purchases>();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void Awake() { }
#else
    private async void Awake()
    {
        foreach (string id in Store.ProductIds()) idOfProduct[Store.ProductId(id)] = id;

        // Every event first: a purchase left unfinished last time can arrive as soon as we connect
        store = UnityIAPServices.StoreController();
        store.OnStoreConnected += OnConnected;
        store.OnStoreDisconnected += failure => { connected = false; Debug.LogWarning("Purchases: App Store disconnected: " + failure.Message); };
        store.OnProductsFetched += OnProductsFetched;
        store.OnProductsFetchFailed += failure => Debug.LogWarning($"Purchases: {failure.FailedFetchProducts?.Count ?? 0} products didn't load: {failure.FailureReason}");
        store.OnPurchasesFetched += OnPurchasesFetched;
        store.OnPurchasesFetchFailed += failure => Debug.LogWarning("Purchases: couldn't fetch past purchases: " + failure.Message);
        store.OnPurchasePending += OnPurchasePending;
        store.OnPurchaseConfirmed += order => { if (order is FailedOrder f) Debug.LogWarning($"Purchases: finishing failed: {f.FailureReason} {f.Details}"); };
        store.OnPurchaseFailed += OnPurchaseFailed;
        store.OnPurchaseDeferred += order => Finish(order, Outcome.Pending);
        try { await store.Connect(); }
        catch (Exception e) { Debug.LogWarning("Purchases: couldn't connect to the App Store: " + e.Message); }
    }
#endif

    private void OnConnected()
    {
        connected = true;
        var products = new List<ProductDefinition>();
        foreach (string productId in idOfProduct.Keys) products.Add(new ProductDefinition(productId, ProductType.NonConsumable));
        store.FetchProducts(products);
    }

    private void OnProductsFetched(List<Product> products)
    {
        productsReady = true;
        Debug.Log($"Purchases: {products.Count} of {idOfProduct.Count} products loaded");
        PricesLoaded?.Invoke();
        store.FetchPurchases();   // anything bought before, on this phone or another
    }

    /// <summary>Purchases already finished: the server gets any it doesn't show yet (one request for them all)</summary>
    private void OnPurchasesFetched(Orders orders)
    {
        var known = new List<KeyValuePair<string, string>>();
        foreach (ConfirmedOrder order in orders.ConfirmedOrders)
            foreach (string id in ItemsOf(order)) known.Add(new KeyValuePair<string, string>(id, order.Info?.Apple?.jwsRepresentation));
        Wallet.SendPurchases(known);
    }

    /// <summary>Paid for: the player has it now; the App Store is told it's delivered once the server has it</summary>
    private void OnPurchasePending(PendingOrder order)
    {
        string jws = order.Info?.Apple?.jwsRepresentation;
        var ids = ItemsOf(order);
        int left = ids.Count;
        bool allSaved = true;
        bool asked = false;
        foreach (CartItem item in order.CartOrdered.Items())
            if (item.Product != null && waiting.ContainsKey(item.Product.definition.id)) asked = true;
        foreach (string id in ids)
        {
            bool had = Store.OwnsId(id);
            Wallet.RecordPurchase(id, jws, saved =>
            {
                allSaved &= saved;
                if (--left == 0 && allSaved) store.ConfirmPurchase(order);
            });
            if (!had) Owned?.Invoke(id);
            if (!had && !asked && !restoring) Redeemed?.Invoke(id);
        }
        if (ids.Count == 0) store.ConfirmPurchase(order);   // nothing of ours in it
        Finish(order, Outcome.Bought);
    }

    private void OnPurchaseFailed(FailedOrder order)
    {
        bool cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled || order.FailureReason == PurchaseFailureReason.OrderCancelled;
        if (!cancelled) Debug.LogWarning($"Purchases: purchase failed: {order.FailureReason} {order.Details}");
        Finish(order, cancelled ? Outcome.Cancelled : Outcome.Failed);
    }

    /// <summary>The Store ids an order is for</summary>
    private List<string> ItemsOf(Order order)
    {
        var ids = new List<string>();
        foreach (CartItem item in order.CartOrdered.Items())
            if (item.Product != null && idOfProduct.TryGetValue(item.Product.definition.id, out string id)) ids.Add(id);
        return ids;
    }

    private void Finish(Order order, Outcome outcome)
    {
        foreach (CartItem item in order.CartOrdered.Items())
        {
            if (item.Product == null || !waiting.TryGetValue(item.Product.definition.id, out Action<Outcome> done)) continue;
            waiting.Remove(item.Product.definition.id);
            done?.Invoke(outcome);
        }
    }

    // ───────── For the menus ─────────

    /// <summary>The item's price in the player's own currency, or the fallback until the App Store answers</summary>
    public static string Price(string id, string fallback)
    {
#if UNITY_EDITOR
        return fallback;   // the editor's fake store has made-up prices
#else
        if (!Ready) return fallback;
        Product product = instance.store.GetProductById(Store.ProductId(id));
        string price = product?.metadata?.localizedPriceString;
        return string.IsNullOrEmpty(price) ? fallback : price;
#endif
    }

    /// <summary>Opens Apple's purchase sheet for an item; done says how it went</summary>
    public static void Buy(string id, Action<Outcome> done)
    {
        Product product = Ready ? instance.store.GetProductById(Store.ProductId(id)) : null;
        if (product == null || !product.availableToPurchase) { done?.Invoke(Outcome.Unavailable); return; }
        instance.waiting[product.definition.id] = done;
        instance.store.PurchaseProduct(product);
    }

    /// <summary>
    /// Apple's sheet for typing an offer code. What a code unlocks arrives like a purchase (Redeemed);
    /// false if the App Store isn't connected or this isn't an iPhone.
    /// </summary>
    public static bool ShowRedeemSheet()
    {
        var apple = instance != null && instance.connected ? instance.store.AppleStoreExtendedPurchaseService : null;
        if (apple == null) return false;
        apple.PresentCodeRedemptionSheet();
        return true;
    }

    /// <summary>Asks the App Store for everything this Apple ID has bought; each one is granted as it comes back</summary>
    public static void Restore(Action<bool> done)
    {
        if (instance == null || instance.store == null || !instance.connected) { done?.Invoke(false); return; }
        instance.restoring = true;
        instance.store.RestoreTransactions((ok, error) =>
        {
            instance.restoring = false;
            if (!ok) Debug.LogWarning("Purchases: restore failed: " + error);
            if (ok) instance.store.FetchPurchases();
            done?.Invoke(ok);
        });
    }
}
