# WW2 Strategy Prototype

Unity 6 LTS turn-based strategy prototype built from the project design documents in this repository. The current scene is a playable radius-14 hexagonal battlefield with 631 cells, six cities and thirty-eight units.

## First playable version

- Left-click a blue unit to select it, an object to inspect it, or a green tile to move.
- Right-click an attack target to attack immediately. Hold and drag the right or middle mouse button to pan; use the wheel to zoom.
- Cyan animated diamonds mark units that can still act; the selected unit uses a yellow pulse.
- Selecting a unit smoothly centers the camera once. Later inspection and UI refreshes preserve manual pan and zoom.
- Hover a highlighted tile or target to preview movement cost or combat damage and counter-damage.
- Movement, attacks, garrisoning and visible enemy actions produce a short map animation and a temporary result card.
- Movement can be split across multiple actions and uses terrain costs; infantry always pays one point per tile. Normal units spend all remaining movement when they attack; armor may continue moving after its single attack.
- Movement range is clipped to currently visible cells. Moving may reveal new cells, which become eligible for a later movement action if the unit still has action points.
- City flags, territory tint and restrained border lines keep ownership readable. Garrison coverage appears when its unit is selected.
- Selecting a unit shows its supply reach and the currently used supply path. Going beyond the path range applies escalating attack, defense and movement penalties.
- Each city-border tile has one wall entity. The strongest covering garrison adds its live defense and retaliation without a hidden synergy multiplier; artillery bombards the wall and that garrison together.
- Hovering an attack target shows damage, retaliation, health strength, support and flanking modifiers. Artillery cannot retaliate and prevents retaliation when it attacks.
- Eligible **驻扎** and **占领城市** commands appear beside the selected map unit as contextual actions instead of being buried in the side panel.
- Entering an enemy center disables the city. Infantry gets **占领城市** immediately; other units are selected automatically with the command available next round. Occupation consumes all action points.
- Damage and counter-damage appear as animated map numbers instead of relying on the log.
- Each side starts with one level-two city, two level-one cities, two enterprises and two factories. The connected opening network yields 30 economy and 18 industry per turn.
- The mirrored terrain layout creates two main fronts and a central maneuver zone: mountain ridges split lanes, forests protect flanks, hills provide firing positions and marshes slow mechanical breakthroughs.
- With units removed and city gates open for distance calibration, each side's supply field covers its complete half and both opposing front cities, but not the opposing rear city.
- Select a friendly city to recruit infantry or medics, or a friendly factory to manufacture artillery or armor. Production is immediate, resource-limited, and new units act from the following turn.
- Use **结束回合** to refresh movement, supply effects and eligible wall recovery, then let the red AI take its turn.
- Capture the enemy city center with a valid supply connection, or destroy the opposing field army, to win.

The map always shows terrain, roads, cities and ownership. Enemy units are only rendered inside current blue visibility. The side panel exposes supply, suppression, garrison and action state without adding new game rules.

Every computer turn writes a persistent decision replay under `AI-Replays` in Unity's persistent data directory. It records decisions, reasons, results, army snapshots and units left idle at turn end.

## Structure

- `Assets/WW2/Core` — pure C# simulation, rules, pathfinding, supply, combat, visibility and AI entry point.
- `Assets/WW2/Runtime` — Unity map rendering and prototype bootstrap.
- `Assets/WW2/Editor` — project scene generation and editor tooling.
- `基本底层设定集.md` — agreed bottom-level rules.
- `系统设计.md` — system architecture.
- `数值设定.md` — first playable numerical baseline.
- `AI行动策略.md` — computer-player strategy.

## Open

Open this directory as a Unity project with Unity `6000.3.23f1`. On first import, the editor creates `Assets/Scenes/Main.unity` and registers it as the startup scene.
