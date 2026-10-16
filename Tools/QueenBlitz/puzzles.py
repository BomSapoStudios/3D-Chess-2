"""The ten test puzzles. Run this file to re-check every par with the solver."""
from solver import Board

# name, size, queen start (None: the player places it), pawns, blockers, tip, ice (optional)
PUZZLES = [
    ("Straight Shot", 4, (0, 1, 1), [(1, 1, 1), (2, 1, 1), (3, 1, 1)], [],
     "The queen drives straight through every pawn on her line."),
    ("Corner Turn", 4, (0, 0, 0), [(1, 0, 0), (2, 0, 0), (3, 0, 0), (3, 1, 0), (3, 2, 0), (3, 3, 0)], [],
     "Finish one line where the next one begins."),
    ("Stop Short", 4, (0, 0, 0), [(1, 1, 1), (2, 2, 2), (2, 2, 0), (2, 2, 1)], [],
     "You don't have to ride a line to the edge."),
    ("Long Way Round", 4, (2, 0, 2), [(0, 0, 0), (1, 1, 1), (2, 0, 1), (2, 1, 1), (2, 2, 2)], [],
     "The biggest sweep isn't always the best first move."),
    ("Your Move", 4, None, [(1, 2, 0), (2, 2, 1), (3, 1, 1), (3, 2, 2)], [],
     "Place the queen yourself. Where you start matters."),
    ("Roadblocks", 6, None, [(3, 0, 4), (3, 5, 2), (4, 4, 1), (5, 3, 0)], [(1, 1, 5), (2, 4, 4), (4, 1, 4), (4, 3, 2)],
     "Blocks stop the queen. Find a way around them."),
    ("Diagonal Detour", 6, None, [(0, 4, 5), (1, 3, 4), (2, 0, 1), (2, 2, 3), (3, 0, 1), (4, 0, 4)], [(0, 1, 4), (2, 2, 5), (3, 3, 1), (5, 5, 4)],
     "Only one starting square works in three."),
    ("First Frost", 4, (0, 0, 0), [(1, 0, 0), (2, 0, 0), (2, 1, 0), (2, 2, 0), (2, 3, 0)], [(3, 0, 0)],
     "Ice! The queen slides until the edge or a block stops her.", True),
    ("Ice Rink", 6, None, [(1, 1, 4), (2, 4, 4), (2, 5, 4), (4, 1, 4), (4, 5, 4), (5, 1, 4), (5, 5, 1), (5, 5, 2), (5, 5, 3), (5, 5, 4)], [(0, 1, 3), (1, 0, 5), (4, 3, 1), (4, 3, 5), (5, 1, 0), (5, 2, 5), (5, 3, 2), (5, 5, 5)],
     "Use the blocks as brakes.", True),
    ("Black Ice", 6, None, [(0, 0, 5), (2, 2, 5), (3, 0, 2), (3, 3, 5), (3, 5, 5), (4, 3, 1), (4, 4, 1), (4, 4, 5), (4, 5, 4), (4, 5, 5), (5, 5, 3)], [(0, 5, 5), (1, 4, 4), (2, 3, 1), (2, 5, 3), (2, 5, 5), (3, 2, 3), (4, 3, 3), (5, 3, 3)],
     "Only one starting square clears it in five.", True),
]

if __name__ == "__main__":
    for i, (name, n, start, pawns, blocks, tip, *rest) in enumerate(PUZZLES, 1):
        ice = bool(rest and rest[0])
        b = Board(n, pawns, blocks, ice)
        par, line, s = b.solve(start, limit=8)
        greedy = b.greedy(start)
        starts = 1 if start else len(b.optimal_starts(par))
        free = Board(n, pawns, (), ice).solve(start, limit=8)[0] if blocks else None
        print(f"{i:2} {name:16} {n}³ {'ice ' if ice else ''}par={par} greedy={greedy} starts={starts} parWithoutBlocks={free} solution={line}")
