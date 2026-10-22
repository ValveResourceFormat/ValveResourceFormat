---
description: Copy the 3D view of Source 2 Viewer to the clipboard, with a transparent background for models, set up clean shots, and save textures, vector graphics and graphs as PNG or JPG images.
---

# Screenshots and Image Export

Source 2 Viewer copies the 3D view to the clipboard, and saves textures, vector graphics and graphs as images. The examples use files from Deadlock.

## Copying the 3D View

1. Open a model, map or particle system, such as `models/heroes_staging/haze/haze.vmdl_c`, and frame it.
2. Click the 3D view, then press <kbd>Esc</kbd> to clear the selection the click made.
3. Press <kbd>Ctrl</kbd>+<kbd>C</kbd> and paste the image anywhere.

![Haze in the model viewer of Source 2 Viewer, with the 3D view on the right and the shortcut bar at the bottom](./images/screenshots-model-view.webp)

Models are copied on a transparent background. Maps and particle systems are copied as shown, with their background.

The copy is as large as the 3D view. For a larger one, press <kbd>F11</kbd> for full screen before copying.

## Setting Up the Shot

### Level of Detail

Set **Level of Detail** in the sidebar to **LOD 0** to keep the most detailed version of the model when it is small in the view.

![The Level of Detail list of the model sidebar, open with LOD 0 highlighted, above the Render and Display groups](./images/screenshots-model-lod.webp)

### Backgrounds

The **Display** group in the sidebar has **Light Background**, **Solid Background** and **Show Grid**. **Solid Background** gives a flat magenta, or green with **Light Background**, which is the way to get a particle effect on a flat color. **Pause** holds the effect still.

![The Detail Level list and the Display and Playback groups of a particle tab, with Light Background and Solid Background checked and Show Grid unchecked](./images/screenshots-particle-display.webp)

![The copy of the fireplace fire particle system on a flat green background](./images/screenshots-particle-copy.webp)

### Settings

The **Video settings** in **Settings** set **Anti-aliasing**, **Camera FOV**, **Max texture size** and **Shadow quality**. **Camera FOV** applies when you go back to the tab. Reopen the file after changing the others.

## Maps

- Turn off **Tool Entities** above the 3D view to hide the entity markers.
- **Save** and **Restore** in the **Camera** group keep a view to come back to.
- **Map Camera** moves the view to a camera placed in the map.
- **Exposure** sets the brightness.

![The Tool Entities button in the toolbar and the Camera group with a saved camera and the Map Camera list open with intro_camera highlighted](./images/screenshots-map-camera.webp)

## Saving Textures as Images

This example saves Haze's hero card, `panorama/images/heroes/haze_card_psd.vtex_c`.

1. Open the texture and pick what to save in **Channels**. **RGBA (Transparent)** keeps the transparency.

    ![The texture viewer showing Haze's hero card on a checkerboard, with Channels set to RGBA (Transparent) in the sidebar](./images/screenshots-texture-view.webp)

    ![The Channels list of the texture viewer, with RGBA (Transparent) highlighted](./images/screenshots-texture-channels.webp)

2. Click **Save to disk...** or press <kbd>Ctrl</kbd>+<kbd>S</kbd>.

    ![The Save to disk button, highlighted, next to "or Ctrl-C to copy", above the texture size and the Mip level list](./images/screenshots-texture-save-button.webp)

3. Pick **PNG Image** or **JPG Image**, or **EXR Image** for HDR textures, and click **Save**.

    ![The Save an Image File dialog in a folder named Screenshots, with the file name haze_card_psd.png and the Save as type list open showing PNG Image and JPG Image](./images/screenshots-texture-save-dialog.webp)

The image is saved at the full size of the texture. JPG has no transparency, so transparent parts turn black:

![The saved hero card twice: on the left the PNG with its transparent background over a checkerboard, on the right the JPG with a black background](./images/screenshots-texture-result.webp)

## Exporting Vector Graphics

Panorama vector graphics (`.vsvg_c`), such as `panorama/images/builds/citadel_build_tag_damage.vsvg_c`, open in the texture viewer too.

1. Click **Save to disk...**.
2. Pick **SVG** to save the original file, or **PNG** or **JPG** with a **Resolution**.

    ![The Export image window with Format set to PNG, Resolution set to 1024 x 1024 and Output 1024 x 1024 px](./images/screenshots-svg-export-window.webp)

3. Click **Save** and choose where to save it.

    ![The Save an Image File dialog with the file name citadel_build_tag_damage.png and PNG Image as the only file type](./images/screenshots-svg-save-dialog.webp)

## Saving Graphs

Animation graphs, Pulse graphs and entity I/O graphs have a **Save to disk...** button in the sidebar too, which saves the whole graph.

![An animation graph tab of the Haze model, with the Save to disk button highlighted at the bottom of the sidebar next to the graph](./images/screenshots-graph-save.webp)
