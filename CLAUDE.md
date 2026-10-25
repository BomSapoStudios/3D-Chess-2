# 3D Chess

## New Customize items need store images

Whenever a new backdrop, piece set or promotion animation is added, render its App Store
in-app purchase images before calling the work done:

- Render only the new items: `Tools > Store Images > New Items Only` in Unity, or with the editor
  closed: `Unity -projectPath . -executeMethod StoreImageRenderer.RunNewFromCommandLine`. It skips
  every item that already has a promo image, so existing images Brandon has uploaded stay as they are.
  (`Render All` redoes everything; only use it when asked.)
- It writes a 1024x1024 promo image and a 2796x1290 review screenshot per item to
  `~/Desktop/store-images`, named by product ID (`BomSapo.Chess3D.` + `Store.Id(item)`, lowercased).
- For a new promotion, add its best moment to `MidPoint` in `Assets/Editor/StoreImageRenderer.cs`.
- Look at the new images, and share them with Brandon along with the new product IDs.

## Device builds

A Unity export replaces `Builds/iOS` and deletes `Builds/iOS/appstore/` (publish settings).
Use `Builds/device-build.sh`, which backs that folder up to `Builds/appstore-backup`, builds, restores
it and installs on Brandon's iPhone.
