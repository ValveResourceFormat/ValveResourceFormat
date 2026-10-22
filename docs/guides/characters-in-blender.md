---
description: Export a Deadlock hero from Source 2 Viewer to glTF, then pose its skeleton, play its animations and render it in Blender.
---

# Posing Characters in Blender

Characters exported to glTF arrive in Blender rigged, with their skeleton and animations. This guide takes the Deadlock hero Haze to a posed render in Blender 5.2. The same steps work for CS2 agents, Dota 2 heroes and Half-Life: Alyx characters, see [Finding Models](./exporting-models.md#finding-models).

## Finding the Hero

1. In the **Explorer** tab, expand **Deadlock** and double-click `game/citadel/pak01_dir.vpk`.

    ![Explorer with Deadlock expanded and game/citadel/pak01_dir.vpk selected](./images/characters-in-blender-explorer.webp)

2. Type `models/heroes_staging/haze` into the box above the tree and press <kbd>Enter</kbd>. For other heroes, see [Finding a Hero's Model](../games/deadlock.md#finding-a-hero-s-model).

    ![The box above the tree reading models/heroes_staging/haze, and the haze folder expanded with haze.vmdl_c](./images/characters-in-blender-package.webp)

3. Double-click `haze.vmdl_c` to preview her. The **Animation** list in the sidebar plays her animations and shows their frame rate.

    ![Haze in a model tab playing primary_idle, with the sidebar on the left](./images/characters-in-blender-viewer.webp)

## Exporting to glTF

1. Right-click `haze.vmdl_c` and select **Decompile & export**.

    ![Context menu of haze.vmdl_c with Decompile & export highlighted](./images/characters-in-blender-context-menu.webp)

2. Choose an empty folder, set **Save as type** to **glTF** and click **Save**.

    ![Save dialog with Save as type set to glTF and the file name haze.gltf](./images/characters-in-blender-save-dialog.webp)

3. When the window says **Export completed**, click **Close**.

    ![Export progress window saying Export completed](./images/characters-in-blender-export-completed.webp)

## Importing into Blender

### Preparing the Scene

Most Deadlock animations run at 30 frames per second, and Blender's default is 24. Set the frame rate before importing, as changing it later changes the playback speed.

1. Delete the default cube. Keep the camera and the light.
2. In the **Output** properties (the printer icon), set **Frame Rate** to **30**.

    ![Output properties with the Frame Rate list open and 30 highlighted](./images/characters-in-blender-frame-rate.webp)

### Importing the glTF

1. Select **File > Import > glTF 2.0 (.glb/.gltf)**.

    ![Blender's File > Import menu with glTF 2.0 highlighted](./images/characters-in-blender-import-menu.webp)

2. Select `haze.gltf`.

    ![The path of the export folder at the top of Blender's import file browser, with haze.gltf selected](./images/characters-in-blender-file-browser.webp)

3. Under **Bones & Skin**, set **Bone Shape Scale** to **0.3**, so the bones do not [hide the model](./exporting-models.md#bones-look-like-big-spheres-in-blender).

    ![Import options with Bone Dir Blender and Bone Shape Scale 0.30](./images/characters-in-blender-import-options.webp)

4. Click **Import glTF 2.0**.

With the mouse over the viewport, press <kbd>Numpad .</kbd> to zoom to her, and press <kbd>Z</kbd> and pick **Material Preview** to see her textures.

![Haze imported into Blender in Material Preview](./images/characters-in-blender-imported.webp)

In the **Outliner**, `haze` is the armature, with her meshes below it.

## Posing the Character

1. The import leaves the animation `ref` assigned, which resets the bones whenever the frame changes. Click `haze` in the Outliner, open the **Object** properties (the orange square), expand **Animation** and click the **X** next to `ref`.

    ![The Animation panel of the Object properties with ref assigned](./images/characters-in-blender-animation-panel.webp)

2. Switch to **Pose Mode**.

    ![The viewport's mode menu with Pose Mode highlighted](./images/characters-in-blender-pose-mode.webp)

3. Click a bone, press <kbd>R</kbd> and move the mouse to rotate it.

    ![Haze in Pose Mode with her left upper arm rotated](./images/characters-in-blender-posed.webp)

4. To start over, press <kbd>A</kbd> and select **Pose > Clear Transform > All**.

    ![The Pose menu with Clear Transform > All highlighted](./images/characters-in-blender-clear-transform.webp)

::: tip
To start from a frame of an animation, assign the animation as described below, go to that frame and click the **X** again.
:::

## Playing the Animations

1. In the **Animation** panel, click the button at the left of the first row and pick an animation, such as `primary_idle`.

    ![The animation list filtered by idle with primary_idle highlighted](./images/characters-in-blender-action-list.webp)

2. In the **Timeline**, set **Start** to `0` and **End** to the animation's last frame, such as `55`. The orange diamonds mark its keys.

    ![The Timeline at frame 20 with Start 0 and End 55](./images/characters-in-blender-timeline.webp)

3. Press <kbd>Space</kbd> to play.

## Rendering an Image

1. The default world is nearly black. In the **World** properties (the red globe), set **Color** to white.

    ![World properties with the Color set to white](./images/characters-in-blender-world.webp)

2. Frame Haze in the viewport, then select **View > Align View > Align Active Camera to View**.

    ![The View > Align View menu with Align Active Camera to View highlighted](./images/characters-in-blender-align-camera.webp)

3. Select **Render > Render Image** (<kbd>F12</kbd>), and save it with **Image > Save As**.

    ![Blender's Render menu](./images/characters-in-blender-render-menu.webp)

![Haze rendered with EEVEE in a frame of primary_idle](./images/characters-in-blender-render.webp)
