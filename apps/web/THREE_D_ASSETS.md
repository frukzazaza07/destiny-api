# Destiny Shop models

The entered 3D experience renders actual GLB assets, not runtime placeholder body parts or furniture. Source: `scripts/build-shop-models.mjs`. Run `npm run models:build` to regenerate the four self-contained files and measured manifest in `public/models/destiny-shop/initial/`.

## Asset register and provenance

| File | Contents |
| --- | --- |
| `shop.glb` | 32×28 m shop, teak portals, reception, four future-service stations, consultation table, upholstered chairs, cabinets, books, celadon pots, plants, woven lanterns and sunburst art |
| `advisor.glb` | Stylized advisor, face, hair bun, earrings, silk-inspired blouse, trousers, hands, articulated arms and knees |
| `visitor.glb` | Visitor with face, shirt, trousers, shoes, satchel and articulated limbs |
| `tarot-back.glb` | Anonymous ink-blue card with brass geometric border and sunburst |

All geometry is original work authored for this repository by the coding agent at the user's request; source is included. No downloaded/commissioned art, paid asset pack, external texture, font, likeness or motion-capture data is included. These are AI-assisted original project assets, not a claim of exclusive copyright ownership or legal clearance in every jurisdiction. Three.js tooling is MIT-licensed; its package retains the dependency license. Project distribution terms remain the repository owner's decision.

The design uses contemporary Thai-inspired materials and furnishing cues, not a historical reconstruction. No Buddha image, sacred script, royal emblem or religious statue is used. **A human Thai cultural/art review has not occurred**; do not describe these assets as culturally approved or photorealistic. These stylized low-poly models can be revised from source following review.

## Animation and loading

- Advisor clips: `idle`, `greeting`, `seated`, `listening`, `shuffling`, `dealing`, `reveal`, `result`. Runtime selection follows shared Tarot phases and the shuffle sub-step.
- Visitor clips: `idle`, `walk`. Keyboard and touch movement both drive walking. Animated transform nodes form the rig, rather than a deforming skeleton.
- The dynamic Canvas and GLBs are requested only after explicit entry. All four load together; readiness waits for the models. Missing/invalid models or lost WebGL context return to the accessible reading, preserving shared state.
- All 78 anonymous backs are instanced across three material meshes. Identity is never embedded in the GLB or inferred locally; the accessible result uses server-resolved cards.
- Reduced motion stops continuous animation. Audio remains optional and muted by default. No model/animation event sends reading or entitlement data to analytics.

## Optimization and measured budgets

The complete GLB payload is approximately **1.62 MiB**. With 78 cards, the base scene is **77,908 triangles / 59 draw calls**, before optional shadow passes. The manifest contains per-file measurements. Static geometry is merged by material, unused UVs removed, and matching vertices welded into indexed meshes. Shared PBR color/roughness/metalness materials avoid texture downloads and texture memory.

`npm run validate:3d-assets` checks GLB headers, embedded buffers, indexed meshes, PBR materials, required clips, manifest consistency, a 6 MiB initial / 10 MiB total ceiling, and an 80,000 triangle / 60 base-draw-call ceiling. The previous 50-call LOW target was increased to 60 to retain articulated characters. LOW disables shadows and reduces pixel density. STANDARD/HIGH use the same compact geometry with higher resolution and shadow quality.

Meshopt compression, KTX2/atlases, separate geometric LODs and later progressive bundles are **not implemented**. They are not needed for the current asset-byte budget; KTX2 would apply if textures are introduced. 60 FPS desktop / 30 FPS mobile remain targets, not device-lab measurements or guarantees. GLB size excludes JavaScript/physics downloads, GPU memory and rendering overhead.

## Verification and visual inspection

```sh
npm run models:build
npm run validate:3d-assets
npm run models:inspect
npm run typecheck
npm run build
npm run test:smoke
```

`models:inspect` loads exported GLBs in Chrome/Three.js, renders character poses and individual assets, and saves actual-render screenshots to ignored `.tmp/model-previews/`. `tests/shop-models.spec.ts` checks deferred loading, three-zone traversal, quality switching, missing-model recovery and context-loss fallback, and saves in-shop screenshots. A local Chrome installation is required.
