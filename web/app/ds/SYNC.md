# Design system sync

Vendored copy of Matt's Design System. The vendored copy under `web/app/ds` is strictly copy-only and NEVER edited by hand. Any fixes, styling updates, or Part C additions must be published to the upstream design kit artifact first, and then brought into this repository via a clean re-sync.

- Source: Design System artifact, <https://claude.ai/artifact/E2SU2aXqnLZ5r9tu5VE9Mm>
- Kit version: 1793400839-f2d7
- Synced: 2026-10-31
- Synced by: Claude (read of the published files, copied unmodified; `project/` prefix removed)
- Namespace in the kit bundle: `MattSDesignSystem_522a7a` (not used here; the app imports the sources in `components/src`)

## Contents

| Path | From kit path | Use |
| --- | --- | --- |
| `tokens.css` | `project/tokens.css` | Semantic `--ds-*` tokens, light and dark. Load first. |
| `tokens.json` | `project/tokens.json` | Token source of truth, for lint rules and docs. |
| `components/bundle.css` | `project/components/bundle.css` | Focus ring, reduced motion, motion tokens. Load after `tokens.css`. Never link `tokens.css` alone. |
| `components/src/**` | `project/components/src/**` | Component sources (React, JSX). |
| `components/<Name>/<Name>.d.ts`, `components/index.d.ts` | same | Prop types. |
| `components/<Name>/README.md` | same | Usage notes. |
| `fonts/*.woff2` | `project/fonts/*` | Inter and JetBrains Mono, referenced by `tokens.css`. |
| `README.md` | `project/README.md` | The kit's brand book. |

Not vendored: `bundle.js` (the preview runtime), `preview.html` cards, `ui_kits/`, `templates/`, `archived/`.

## Status

Part C composites are NOT in this sync yet: DataTable, Toolbar, AppShell, EmptyState, Skeleton, Toast, Menu, Tooltip.
They are published to the kit first and then re-synced here. Do not build local versions of them.

## Re-sync

Ask Claude to re-export from the kit. Update the version, date and hashes above, and review the diff like any other change.

## File hashes (sha256, as published)

acdc9667f15791dcaa11574551516de6de525f0c220a7e6904e58fea6ad1f594  README.md
a1e03593fd5415966ab8c18a282e5383446e8d631f4913a8877c4b05d113bda4  components/Alert/Alert.d.ts
d30c1553dec8a052133d702b544cc63ce611e45db838d114082ff8333affe815  components/Alert/README.md
f9462f787019e2c3989bf9c85fd19df1c89d4a433ff6aedf2aae8c7db2a10eda  components/Avatar/Avatar.d.ts
08c995656433c0a4e9f4e115bd72d16a0313e8fe5dd4b6c5eb2190a77690423c  components/Avatar/README.md
55dfda0d05b69162e328958242c95a8f65a15f8fabdbb0b6472b0628d63b4c3e  components/Badge/Badge.d.ts
a8abf2d5e374af99d5e8402d40f2f449701aff213e5f148b62ae01fdd062d4f6  components/Badge/README.md
b3dec14da444c4535f24e2282c1806324c6ca17ce78c26df9ec173acd77943d2  components/Button/Button.d.ts
64e5d3612c7f7a255ae0fe1f7a19400e65ae803117969edab2473fcd65fc3bb1  components/Button/README.md
4665ed563eea8d883b584a386335e9327f338ad8391b67be0f40fce94bc4a7de  components/Card/Card.d.ts
2ef1733ee62e885421561b8ebf023c42f7c954a487bc3da1dbe52463947f9754  components/Card/README.md
406a141f91cef020507b24a87760c899fba32304142d814682564a54e4a7addc  components/Checkbox/Checkbox.d.ts
c701fb944fe5d2c125a23141a4f3a096071e1a90c745d824106103cf7f5478cd  components/Checkbox/README.md
2f34a4a339a22b66b18a7f5169dd695f8a708954cb6147385d62c4b34b7faf5d  components/Input/Input.d.ts
9c3a471a64e5d0f414c492818bdf5d1a57b90025644c0f926c9ac3b356388a77  components/Input/README.md
45a5d291cc2b87433959d409aba205abaedebe7bf42e774cb77874f4aa7280d5  components/Modal/Modal.d.ts
2195365d1f5cf3ac518aceee64a67139141ab7079ca140fc791c58da4a7e21a5  components/Modal/README.md
b5e5749b79aea74578d737fe46ab4684669c1971a51a7a8052ca1c3fd0fc33c7  components/Radio/README.md
2c90a901a3a8118dc6c7ac76cd53b0760271a8f84c179112ce4a26ac96afefb6  components/Radio/Radio.d.ts
f41bb1bf9d32b0a17a4fd6f2099c7952374d239806d236fab3638a6eae9b6f76  components/Select/README.md
9365e1e8af64320e1340d58c7cd799d4b7499e6093dda59462707737c240eb1b  components/Select/Select.d.ts
2185b4aa5d4b8c5aa1d00cd0b1afb67711ef237415575ced9d48872f646172ae  components/Switch/README.md
7a587628e68c9e393de8f59fc1df0071ba220ac38e1acb0d892881b3f6ee240a  components/Switch/Switch.d.ts
f4f42198ef155fc6d85555df291da05f1ac9290f0dbd62dc8cd05cb2a494a5a3  components/Tabs/README.md
f11f85be0d9569148066e58437c87b4abc2405e7429fbe7016f27eb9c90ef75e  components/Tabs/Tabs.d.ts
5a2e07f7701651f4ac225d55f6950fb02213c327ce9955f4c7adf32d4114e87f  components/Textarea/README.md
57a29359347a28b4d0dbcf079a607d1eaf4e92a5e07991e5404328179e4938d4  components/Textarea/Textarea.d.ts
96e6dc7a093c21e3fe922557de70d6818480b2d1139d3e5ff32262d44b0fb49c  components/bundle.css
6c4f0c0260d267fb15805cf5afc2872e89fc4550a113cdba3dd922d6913eccd1  components/index.d.ts
262e4944ee2372ce938ed452d71a5ef63d10c3ab84ceb1e295ec5e9c1896132b  components/src/feedback/Alert.jsx
b75efcfcb2c2fcf9d776da42daec9eae48aac69e3943bf6d62eb949b7032df43  components/src/feedback/Avatar.jsx
8685434b69e69e824b6f186c2865bca62878b2538cdb5d3e1ffc16ce71f1bb3f  components/src/feedback/Badge.jsx
bad29af9c5aefd6ec05a1e2a71ae83f9a21b81e0e1356f6ea69071f321d159a1  components/src/feedback/Card.jsx
dc9f20f56c117d099f99af18a02ceb694e22bbac4b5c86d8f61db828bb1f0c4a  components/src/forms/Button.jsx
580bf57ae74ad37dd9d348d7b32f7aa8524dc172f7a67e87d2f23f72de5aa270  components/src/forms/Checkbox.jsx
8f6a1b3d86e5a80dcc007afe6822a0137b342d3069669b1264ac049edc76c965  components/src/forms/Input.jsx
87b76dcc9c6c9a6285267dc7cdb7b30a47ba411c5fb31f225a5ff183a0f4e33e  components/src/forms/Radio.jsx
0ff0222d7ffc08ae7356cc938d108960ba2b4ce397969f2dc310711741bc0898  components/src/forms/Select.jsx
40b390523bc0f4f56a7cd387a55b11f9f2b6b9841f803ecb348138eb517736d7  components/src/forms/Switch.jsx
45d37313748fb92b8c68d77bfeac2aabce096090138b3fbfa85dd07981b02f59  components/src/forms/Textarea.jsx
7b4a7a17182a5c495e47c6c14501633eeabacf6b9fe16e6581311dea1ae6ebc9  components/src/navigation/Tabs.jsx
1cd4b963407539bd6de7e1ec2b9c8126e88bb868ad1adeb37442b90976c046b8  components/src/overlay/Modal.jsx
3100e775e8616cd2611beecfa23a4263d7037586789b43f035236a2e6fbd4c62  fonts/inter-latin-wght-normal.woff2
18be452724bfdc236c074ca94a249a7f41a86752c7d04ab258ce9ed5651f6a7e  fonts/jetbrains-mono-latin-wght-normal.woff2
9d9e26b0e8b3a68ef2e219a86504e0fbed673b76befd73cbca73013a48c70fbc  tokens.css
455cbdd7cde3a78c74ac331bef3b35b68b825f838d665333f454b01f820b6a4b  tokens.json
