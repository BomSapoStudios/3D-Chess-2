using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Plays a button's sound the moment it's touched instead of when the finger lifts, so the sound
/// isn't late. The button's own click then skips its tap (Sfx.Tap).
/// </summary>
public class PressSound : MonoBehaviour, IPointerDownHandler
{
    public bool back; // the falling "back" bloop instead of the tap

    private Selectable selectable;

    private void Awake() => selectable = GetComponent<Selectable>();

    public void OnPointerDown(PointerEventData eventData)
    {
        if (selectable != null && !selectable.IsInteractable()) return;
        Sfx.Press(back);
    }

    /// <summary>
    /// Gives every button under root a PressSound (buttons that already have one keep it). Skips
    /// switches (they blip when they flip) and invisible tap catchers such as a dimmed background.
    /// </summary>
    public static void AddAll(Transform root)
    {
        foreach (Button b in root.GetComponentsInChildren<Button>(true))
        {
            if (b.GetComponent<PressSound>() != null || b.name == "Switch" || b.transition == Selectable.Transition.None) continue;
            b.gameObject.AddComponent<PressSound>();
        }
    }
}
