# ArtV6 component rules

Visual source: `ConceptArt/ww2-gameplay-visual-master-v6-simplified-shapes.png`.

## Layering

1. Unity draws the exact shared hex ground geometry and flat base color. This is the production base layer.
2. `Terrain/forest-overlay-v2.png`, `hill-overlay-v2.png`, `mountain-overlay-v2.png`, and `marsh-overlay.png` are the retained transparent runtime overlays.
3. Roads, buildings, walls, units, interaction masks, and UI remain separate layers.

## AI-painted units

The four files in `Units/` are transparent, camera-matched gameplay sprites derived from the approved
unit concept board. Their runtime pivots sit at the painted ground contact, while faction ownership stays
on the physical base and selection ring instead of tinting the whole model blue or red.

| Asset | Role |
|---|---|
| `main-infantry.png` | Two-person main infantry squad |
| `medic.png` | Single medical support soldier |
| `light-artillery.png` | Simplified light field gun |
| `light-armor.png` | Simplified light tank |

## Locked style

- Opaque hand-painted shapes; no outlines.
- At most three value steps per object.
- No realistic surface detail, texture noise, fog, grime, or material rendering.
- All map components use the same upper three-quarter light direction.
- Terrain overlays stay safely inside a cell; road and river connectors alone may meet exact edge-midpoint sockets.
- The grid is rendered once by Unity and is never baked into an asset.

## Runtime validation

The retained set contains four terrain overlays and four unit sprites. Unity's art diagnostic loads every retained runtime texture, and the shared program-drawn hex ground prevents alpha seams and repeated base-tile brush marks.

The approved component reference remains at `ConceptArt/artv6-component-assembly-preview-v3.png`.
