// ─────────────────────────────────────────────────────────────────────────────
// Pawns: every number the server uses to pay and charge them. The app shows the same values
// (PawnBank.cs, Store.cs, QueenBlitz.cs); when one changes, change both.
// ─────────────────────────────────────────────────────────────────────────────

export const ECONOMY = {
  /** Pawn prices by item kind (an item id is "<kind>.<name>", lowercase) */
  prices: { set: 4500, backdrop: 3000, promotion: 1000, blitz: 4500 } as Record<string, number>,
  /** Queen Blitz: what Auto-place costs */
  autoPlace: 1,
  /** Queen Blitz: the most a puzzle can pay (one pawn per pawn on the board; the biggest puzzle has 39) */
  blitzMaxPawns: 40,
  /** Zombies!: pawns per zombie destroyed, by brain size (a run's total is rounded down) */
  zombieRates: { easy: 1, medium: 1.5, hard: 2 } as Record<string, number>,
  /** The daily bonus: pawns each account can claim once per day, the day starting at the player's midnight */
  dailyClaim: 50,
  /** The furthest a phone's clock may be from UTC for the daily bonus (minutes; real zones are -12h to +14h) */
  maxUtcOffsetMinutes: 14 * 60,
  /** Zombies!: the most it pays per account per day (UTC) */
  zombieDailyCap: 100,
  /** Zombies!: more kills than this in one run isn't believable */
  zombieMaxKillsPerRun: 2000,
  /** Moving an existing phone's pawns to the server: at most this many come along */
  migrateCap: 10_000,
  /** Items a phone can bring along when it first syncs */
  migrateMaxItems: 200,
  /** Earnings in one sync (the app queues them while offline) */
  maxEventsPerSync: 100,
  /** App Store transactions in one call (a restore sends them together) */
  maxTransactionsPerCall: 40,
} as const;

/**
 * Everything sold is "<kind>.<name>"; ids are lowercase (App Store product ids are "BomSapo.Chess3D." + id).
 * "bundle.everything" (Unlock Everything, money only) owns every item and pack; the app honors it.
 */
export const ITEM_ID = /^(set|backdrop|promotion|blitz|bundle)\.[a-z0-9-]{1,40}$/;
export const EVERYTHING = 'bundle.everything';
/** A Queen Blitz puzzle: "<pack>.<index>" (dailies are "daily-YYYY-MM-DD.0") */
export const PUZZLE_ID = /^[a-z0-9-]{1,40}\.[0-9]{1,4}$/;
/** Request and run ids the app makes up (GUIDs) */
export const CLIENT_ID = /^[A-Za-z0-9-]{8,64}$/;

export const PRODUCT_PREFIX = 'bomsapo.chess3d.';

/**
 * Pawn top-ups (Consumable App Store products). They're only given away with App Store offer codes:
 * the app never shows them for sale. Each transaction adds its pawns once; they're never owned items.
 */
export const PAWN_PRODUCTS: Record<string, number> = { 'pawns.50': 50, 'pawns.100': 100 };

/** The pawns an App Store product adds, or null if it isn't a pawn top-up */
export function pawnsOfProduct(productId: string): number | null {
  const id = productId.toLowerCase();
  if (!id.startsWith(PRODUCT_PREFIX)) return null;
  return PAWN_PRODUCTS[id.slice(PRODUCT_PREFIX.length)] ?? null;
}

/** The pawn price, or null if pawns can't buy it (unknown items and Unlock Everything) */
export function priceOf(item: string): number | null {
  if (!ITEM_ID.test(item)) return null;
  return ECONOMY.prices[item.split('.')[0]] ?? null;
}

/** The item an App Store product unlocks, or null if it isn't one of ours */
export function itemOfProduct(productId: string): string | null {
  const id = productId.toLowerCase();
  if (!id.startsWith(PRODUCT_PREFIX)) return null;
  const item = id.slice(PRODUCT_PREFIX.length);
  return ITEM_ID.test(item) ? item : null;
}
