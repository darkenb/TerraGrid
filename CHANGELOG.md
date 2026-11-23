# Changelog

## 1.0.2

- New `FixLockedLevels` setting (off by default, **Ctrl+L** in game): on planets that build by height, a spot once covered with foundation a level higher stayed a step above the foundation around it, and no brush could bring it down. With the setting on, painting lowers such spots to the new foundation.

## 1.0.1

- Much faster painting of large areas. The game re-rendered the whole planet's heightmap and recounted its land after every painted block; while painting this now happens at most twice a second and once at the end.
- The blue pending-tile markers are built once per block instead of being rebuilt for every remaining tile several times a second.
- Lighter per-tile checks when an area is confirmed.

## 1.0.0

First release.
