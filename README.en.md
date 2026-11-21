# jrf_vrc_parts

Small Modular Avatar parts for VRChat. JRF made them with Claude Code (Claude Opus 5.5) for covering and photographing VRChat worlds, and uses them. Similar things exist elsewhere, but here they are anyway.

[日本語](README.md)

| Part | In short |
|---|---|
| [MyAirPlatform (air chair)](#myairplatform-air-chair) | Put a world-fixed chair in the air and let **other people** sit in it |
| [MyTransparent (transparency)](#mytransparent-transparency) | Hide yourself from a menu, and come back properly on other people's screens too |
| [MyLight (light)](#mylight-light) | A light for photos: a spotlight and a light that hits "only people" |

Download from [Releases](https://github.com/JRF-2018/jrf_vrc_parts/releases) (no login needed). Each part is its own unitypackage. Releases have a separate tag for each group; these three are `myparts-v<version>` (latest: [myparts-v0.1.0](https://github.com/JRF-2018/jrf_vrc_parts/releases/tag/myparts-v0.1.0)).

## Requirements

- Unity 2022.3, VRChat SDK (Avatars)
- [Modular Avatar](https://modular-avatar.nadena.dev/) (install with VCC, ALCOM, etc.)
- Made with: Unity 2022.3.22f1, VRChat SDK 3.10.6, Modular Avatar 1.19.0, NDMF 1.15.0

## Usage (all parts)

1. Import the unitypackage (it goes into `Assets/Avatar/<part name>/`).
2. Drag the prefab inside it onto your avatar in the Hierarchy (directly under the avatar root).
3. Upload as usual.

**The menus are in Japanese only.** Each part adds one item to the top of the Expressions menu.

## MyAirPlatform (air chair)

Puts a VRC Station (a chair) in the air in front of your avatar and fixes it in the world. **Other people** click it to sit. JRF's use: the camera person puts out the chair, and the model sits in the air to be photographed.

- **You cannot sit in it yourself.** In VRChat you cannot click a station on your own avatar (your own avatar is on the PlayerLocal layer, which interaction ignores).
- Menu 空中の椅子 (air chair):
  - 椅子を出す (put out chair, toggle): fixes the chair in front of you at that moment. Turn it off and on to place it again.
  - 高さ(20 m) (height, radial): 0 = at your feet, 1 = 20 m up. Works after placing, too.
  - 前へ (forward, radial): 0 to 5 m forward. Default 0.3 (1.5 m).
  - 色を付ける (color, toggle, default ON): an orange plate where you sit. Turn it off for photos.
- Synced parameters: 19 bits (`MyAirPlatform/On`, `Replace`, `Height`, `Forward`, `Color`).

Notes:

- **At height 0 the chair (and the plate) is slightly under the ground.** Raise it a little after putting it out.
- **For about 2 seconds after you place it again, other people may still see the chair at the old place.** It fixes itself after 2 seconds.
  - While another person is not looking at you (the chair's owner), their client seems to stop updating where the chair follows you, and a re-placed chair could stay frozen at the old place. After many tests we found that "one change of a synced parameter after the chair has settled" makes it re-place even for people who are not looking. So 2 seconds after you put out the chair, the avatar itself flips a hidden parameter (`MyAirPlatform/Replace`) once. We checked that it stays right for people not looking even when toggling randomly or rapidly.
- Someone who joins while a person is sitting may see that person at a strange place (such as the world origin). This is reported as a known VRChat issue.
- If you change the height while someone is sitting, the sitter may not see the new height until they move their view once.

## MyTransparent (transparency)

The menu toggle 透明 (transparent) hides your avatar's `Body` (the mesh directly under the avatar root). VRoid avatars converted with VRM Converter for VRChat have a single `Body` mesh, so it works as is. For other avatars, change the target of the MA Object Toggle to the meshes you want to hide.

- Synced parameters: 1 bit (`MyTransparent/On`).
- It contains a **fully transparent box that is always shown** (30 cm at the hips, alpha 0). When an avatar draws nothing at all, other people's clients seemed to stop updating its animation, and turning transparency off did not bring the avatar back on their screens (until an avatar reset). With this box, other people see you again as soon as you turn it off. The box does not show on screen or in photos.

## MyLight (light)

Puts a photo light in front of your neck (on the head it was misplaced on your own screen, because VRChat shrinks your own head). It is for lighting a model in dark places.

- Menu ライト (light):
  - スポット (spot, toggle): a spotlight forward.
  - 人物だけ (people only, toggle): a point light that lights only avatars (players). Floors and walls do not get brighter.
  - 影 (shadow, toggle): casts shadows (not shown if shadows are off in the viewer's graphics settings).
  - 強さ (intensity), 高さ (height), 範囲 (range), 角度(スポット) (spot angle), 上下の向き(スポット) (spot pitch) (radials): height is -1.5 to +4 m, pitch is straight up to straight down.
- Synced parameters: 49 bits. Radial values are saved.
- On other people's screens the light may be hidden by their Safety settings.
- Hint: around 0.75 for 人物だけ makes white clothes and skin clearly brighter.

## Source

`src/` has the editor scripts (`*Builder.cs`) that build each prefab (run in Unity batch mode). The comments keep the history of what we tried. They are not in the unitypackages.

## Author

JRF ( http://jrf.cocolog-nifty.com/statuses , Twitter (X): @jion_rockford )
AI

Claude Opus 5.5 wrote most of it.

## License

MIT License (LICENSE).
