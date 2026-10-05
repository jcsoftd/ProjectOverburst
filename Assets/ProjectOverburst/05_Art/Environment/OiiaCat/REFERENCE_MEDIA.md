# OIIA reference media

The Hideout cat uses the locally supplied W&W reference video's audio and first still frame. The native timing asset records the 30 FPS photo/spin transitions; runtime uses the AudioSource sample position as its clock. The existing credited 3D cat remains the spinning visual.

Local media dependencies are under `Assets/ThirdParty/OiiaReference` and follow the project's existing ThirdParty exclusion policy:

- `OiiaReference.wav` and its `.meta`: stereo PCM, 44,100 Hz, 5,937,330 samples, 134.633333 seconds.
- `OiiaCat_Still_Reference.png` and its `.meta`: unchanged first reference frame, 640 × 360. The photo shader crops the cat bounds and keys out green while rendering.
- `MAT_OiiaReferencePhoto.mat` and its `.meta`, and the folder `.meta`.

Restore those files to the same paths with their original `.meta` files before opening the prefab. The local reference binding operation preserves a restoration copy under its artifact output's `Restore/Assets/ThirdParty` directory. Public media redistribution has not been verified; the external media bytes and dependent material are excluded from public Git.

To bind another restored copy of the same source, run `HideoutOiiaReferenceBuilder.Apply(sourceDirectory)` from the idle Editor. Its source directory must contain `Prepared/OiiaReference.wav`, `Prepared/timeline.json`, and `Frames/OiiaCat_Still_Reference.png`. The builder creates/imports assets through Unity APIs, preserves the installed scene instance, and saves the prefab. The menu entry uses the project-relative local artifact directory.

The same 50 confirmed melee contacts trigger the club presentation; Escape stops the audio, destroys temporary cats/camera/render texture, restores the still photo, and resets the counter.
