# Terra Incognita
Eradicate the natives. Repeat our history.

[Devlog(in Korean)](https://velog.io/@stimpack/series/Terra-Incognita)

## Unit commands

- Right-click ground: Move. Right-click an enemy: Attack.
- A + right-click: Attack Move.
- S: Stop. The unit holds its current position but tracks enemies it senses.
- H: Hold Position. The unit attacks enemies in attack range without pursuing them.
- P + right-click: Patrol between the unit's current position and the clicked position.

Units automatically acquire enemies within their sense range for Attack Move, Stop, and Patrol orders. They stop tracking a target beyond their tracking range. Attack and sense/tracking ranges are configured on each unit's `UnitData`.
