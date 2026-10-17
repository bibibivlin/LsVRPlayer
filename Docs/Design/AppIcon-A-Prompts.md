# A 方案应用图标提示词

使用内置 image_gen；参考图为 [已选定的 A 方案](AppIconConcepts-2026-10-17/A-LPlay.png)。标准/兼容图标直接复用该图，未改动像素；自适应图标透明前景由下面两步生成。背景为程序生成的纯色 #0A1A23，不包含 AI 图像内容。

## 透明前景与安全区

```text
Use case: background-extraction / precise-object-edit.
Input image 1 is the EDIT TARGET: the approved original A icon for L's VRPlayer. Preserve its exact recognisable L-shaped folded seafoam ribbon with the right-pointing curled play arrow at the bottom, including the tall obliquely-cut upright, dark inner triangular fold and gentle teal-to-pale-cyan tonal facets.
Primary request: prepare a production TRANSPARENT FOREGROUND layer for an Android adaptive launcher icon. Remove every pixel of the dark navy background and replace it with genuine alpha transparency, also in the hollow negative space. Keep only the approved ribbon symbol. No backdrop, no shadow, no glow, no checkerboard rendered into pixels, no new shapes, no lettering, no container.
Composition is critical: SHRINK the whole existing symbol uniformly and recenter it with a LOT of empty transparent padding. The visible ribbon should occupy ONLY 50% of the square canvas HEIGHT, and roughly 44% of its WIDTH, centred optically. This means a top margin and bottom margin of at least 25% of the canvas, and side margins of at least 28%. The entire ribbon must fit within the central circular safe zone of diameter 60% of the canvas. Treat the large transparent margins as required content. Do not enlarge the symbol to fill the image. Maintain the original proportions and recognisable geometry rather than redrawing or redesigning it.
Produce a square high-resolution PNG with actual transparency. The result will be used as an adaptive foreground placed over the original dark navy colour. Preserve the original seafoam colour, dimensional fold, clean sharp edges; subtly reduce the brightest specular highlight, with no illumination outside the symbol. NO change of concept.
```

## 前景轮廓清理（采用的最终前景）

```text
Use case: precise-object-edit. Image 1 is the edit target, an approved L-shaped seafoam folded ribbon app icon on transparent background, already positioned at the correct small size with large transparent margins. Image 2 is the ORIGINAL APPROVED DESIGN, provided only as a clean-edge shape reference.
Make ONE correction to image 1: professionally clean its alpha silhouette. Remove ALL loose stray cyan pixels and flecks floating in the empty cutout space and around the outer perimeter. Replace the jagged, dirty, eroded cyan fringe and thin dark outline with perfectly clean smooth antialiased vector-like edges. Restore the simple continuous smooth outlines seen in image 2. The empty negative space between the upright and arrow must be completely transparent and contain zero specks. The transparent canvas outside the logo must be uniformly empty.
Preserve image 1's exact logo placement, small size, large padding, original L + curled play silhouette, mint/pale-cyan palette, proportional geometry, inner triangular fold and subtle facet shading. Do not enlarge the symbol. All meaningful artwork must stay within a circle of diameter 60% of the square canvas. No added shadow, glow, text, border, background or new elements. Genuine alpha PNG, not a black or checkerboard backdrop. Only clean the edges and remove debris.
```
