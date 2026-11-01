# Design system sync

Vendored copy of Matt's Design System. The vendored copy under `web/app/ds` is strictly copy-only and NEVER edited by hand. Any fixes, styling updates, or Part C additions must be published to the upstream design kit artifact first, and then brought into this repository via a clean re-sync.

- Source: Design System artifact, <https://claude.ai/artifact/E2SU2aXqnLZ5r9tu5VE9Mm>
- Kit version: 1793458161-0e77 (v17, includes Part C)
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

Part C composites are in this sync: DataTable, Toolbar (`components/src/data/`), AppShell (`components/src/layout/`), EmptyState, Skeleton, Toast, ToastRegion (`components/src/feedback/`), Menu and Tooltip (`components/src/overlay/`). Use them; do not build local versions.
The kit also corrected the prop types of Alert, Input and Select and the stale `base.css` comments in Checkbox, Radio and Switch.
Verified before publishing: 88 jsdom behaviour checks (sorting, paging, keyboard, focus, timers), axe with no violations on a composed page, WCAG AA contrast for every new colour pair in both themes, and the declaration files type-check under strict TypeScript. Also checked by eye in a browser, in light and dark themes: DataTable, Toolbar, AppShell, EmptyState, Skeleton, Toast, Menu (keyboard and focus return) and Tooltip. That check found and fixed a clipped tooltip, a wrapping Badge and the AppShell mobile layout gap.

## Re-sync

Ask Claude to re-export from the kit. Update the version, date and hashes above, and review the diff like any other change.

## File hashes (sha256, as published)

91def45822782c82e3dc4cce23e3241f2a7546639762f001fceab2ecb60cbba6  README.md
f03c3f7e1c89a87a5c67139f80ec1f8bdfa8aea67f98fcd7e6a7c28706927f3a  components/Alert/Alert.d.ts
d30c1553dec8a052133d702b544cc63ce611e45db838d114082ff8333affe815  components/Alert/README.md
a8648be0fcecaf3633c8b2952a22674f83882354954d1bbf43753f3c967fb164  components/AppShell/AppShell.d.ts
be5558c147c9bef7c246b5f8ce6880fc6debc58a2a6ebbfb6d6b11c136f2ce2c  components/AppShell/README.md
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
570c846370e3218b58a1505c9fdad9c9aa71b7553d6127a662acb62f714fd9aa  components/DataTable/DataTable.d.ts
53d78689f30947a2dd70f1d8cefa5ae907d1f6065584ff3055695c2f99076d29  components/DataTable/README.md
aaca10374b6dbe0edf0b0cc74d76d3a5799be318b174baf2f610f659451757f5  components/EmptyState/EmptyState.d.ts
4eb942bde3c7572591203d543f2e3e341cf708eef84a1a564cd38e98534b7849  components/EmptyState/README.md
20b4065c839e50d2c55702a456c69038d86d44041527fa40ca8a96c1ee51fb64  components/Input/Input.d.ts
9c3a471a64e5d0f414c492818bdf5d1a57b90025644c0f926c9ac3b356388a77  components/Input/README.md
aafd5ffab46e9a1005cf8a7bdcbda94a17bf2c796e161facc29fca3c69622de3  components/Menu/Menu.d.ts
0ae6e05eea0731667be11e3df96948941a256b4bc607824dd7207b3a75fbc0e4  components/Menu/README.md
45a5d291cc2b87433959d409aba205abaedebe7bf42e774cb77874f4aa7280d5  components/Modal/Modal.d.ts
2195365d1f5cf3ac518aceee64a67139141ab7079ca140fc791c58da4a7e21a5  components/Modal/README.md
b5e5749b79aea74578d737fe46ab4684669c1971a51a7a8052ca1c3fd0fc33c7  components/Radio/README.md
2c90a901a3a8118dc6c7ac76cd53b0760271a8f84c179112ce4a26ac96afefb6  components/Radio/Radio.d.ts
f41bb1bf9d32b0a17a4fd6f2099c7952374d239806d236fab3638a6eae9b6f76  components/Select/README.md
333c89eda09ecedefb10f17ab531ae7389b55d7cbeceed3f7938d4b78e03e85e  components/Select/Select.d.ts
1e4cfe147b0d3bba317af70486427e6f00134a8ee9ab65fa88e109a600212843  components/Skeleton/README.md
331cce77db5d6bf7f022d4b0bccc4b0b20ec7f56ea8b959f5a42ab9d70885b71  components/Skeleton/Skeleton.d.ts
2185b4aa5d4b8c5aa1d00cd0b1afb67711ef237415575ced9d48872f646172ae  components/Switch/README.md
7a587628e68c9e393de8f59fc1df0071ba220ac38e1acb0d892881b3f6ee240a  components/Switch/Switch.d.ts
f4f42198ef155fc6d85555df291da05f1ac9290f0dbd62dc8cd05cb2a494a5a3  components/Tabs/README.md
f11f85be0d9569148066e58437c87b4abc2405e7429fbe7016f27eb9c90ef75e  components/Tabs/Tabs.d.ts
5a2e07f7701651f4ac225d55f6950fb02213c327ce9955f4c7adf32d4114e87f  components/Textarea/README.md
57a29359347a28b4d0dbcf079a607d1eaf4e92a5e07991e5404328179e4938d4  components/Textarea/Textarea.d.ts
fcc4921595a23c8401106e90cd369c0058773b2fa3f2a725ed2f3fdab57bf2ef  components/Toast/README.md
21ea8cd2f612f6afb419ed97e752bc8e49f7f048d2790e1fb2e1a03cb88731ba  components/Toast/Toast.d.ts
64b98987f2243828db1a78cc264455fdc61bbb716cf6247f03d5959381dd46d3  components/ToastRegion/README.md
d8707a15881dd16110e78f0d651447fba11e81d68cfb34863b0164eb5307b1c0  components/ToastRegion/ToastRegion.d.ts
6a780c2d913e16a7c68724363b8d6323b721cdb3dc69c345ebdd3f491688ce3f  components/Toolbar/README.md
fb4e796b9c14102892986802ee1e6371344ed3b225f04b38ca4f37edcf2f6223  components/Toolbar/Toolbar.d.ts
b3de70fa3b0f3f86ab939d66dc87e00df87503df06b97581113b45cb7f567eb4  components/Tooltip/README.md
726433458f49c1a5a2039ac5e99d591b3cfa88b3f2905b14d1bf068379ac9ed2  components/Tooltip/Tooltip.d.ts
49708c300d9bfbc3a30b8362dfca28fbb49be2bb1b2d2ec8cecf768c666c82dd  components/bundle.css
fe551749ee94299d0d8566024b7c4b829b117ee2e33e290ff3de275aea128941  components/index.d.ts
92912e9603486c5b11bd7cc0a24207b5e7e92155deeb863992615a254d204052  components/src/data/DataTable.jsx
c49b3329d0edc42823feb819e3ae34dc9690ca1eef35df52604bcbe6197c753f  components/src/data/Toolbar.jsx
262e4944ee2372ce938ed452d71a5ef63d10c3ab84ceb1e295ec5e9c1896132b  components/src/feedback/Alert.jsx
b75efcfcb2c2fcf9d776da42daec9eae48aac69e3943bf6d62eb949b7032df43  components/src/feedback/Avatar.jsx
d6e30d5c071084cf8c69672f5a9c711949c77317daeda250af204916bb8eb12b  components/src/feedback/Badge.jsx
bad29af9c5aefd6ec05a1e2a71ae83f9a21b81e0e1356f6ea69071f321d159a1  components/src/feedback/Card.jsx
53c977abe4bac5a390c6d569333514abbffc2aa4cc02e839170141fda4cbb01c  components/src/feedback/EmptyState.jsx
948b8cbe702d29ca015713bd7c9b745ba8837b9444503e0341f4bc4a2609f9ac  components/src/feedback/Skeleton.jsx
acbffb30b2a39fbbc6e3ded490b31bed92ed572387d987f56254fbf7f0662eec  components/src/feedback/Toast.jsx
394e4c04f9e102d3401f225a2b803d22b496a41192cfa6242cbbb2f4b587558f  components/src/feedback/ToastRegion.jsx
dc9f20f56c117d099f99af18a02ceb694e22bbac4b5c86d8f61db828bb1f0c4a  components/src/forms/Button.jsx
e0912791a628a45bf13ac49e23ee5adbff84dd2a519c3f78eb593f1c33a57cbc  components/src/forms/Checkbox.jsx
8f6a1b3d86e5a80dcc007afe6822a0137b342d3069669b1264ac049edc76c965  components/src/forms/Input.jsx
55993b0110d25517e668f387fffb92a5427da2f274981c0bde9504cdc1686a62  components/src/forms/Radio.jsx
0ff0222d7ffc08ae7356cc938d108960ba2b4ce397969f2dc310711741bc0898  components/src/forms/Select.jsx
ec9791c40c3daeff8e26844c28e57eb2a51ee3464bfaee01add33a620906dfb0  components/src/forms/Switch.jsx
45d37313748fb92b8c68d77bfeac2aabce096090138b3fbfa85dd07981b02f59  components/src/forms/Textarea.jsx
25bdfa30c4cfa9e3d1c3be27be00ef3bfcfedc238847017aa0c416ad0470940c  components/src/layout/AppShell.jsx
7b4a7a17182a5c495e47c6c14501633eeabacf6b9fe16e6581311dea1ae6ebc9  components/src/navigation/Tabs.jsx
29e9805c03a0465ea00d36f0378830e044492ef0228d544511a7e4e0cc65e76f  components/src/overlay/Menu.jsx
1cd4b963407539bd6de7e1ec2b9c8126e88bb868ad1adeb37442b90976c046b8  components/src/overlay/Modal.jsx
95f1e8a58406012c1ddaebd059706b676ee8b2262e4a2816fc411057bf71d368  components/src/overlay/Tooltip.jsx
3100e775e8616cd2611beecfa23a4263d7037586789b43f035236a2e6fbd4c62  fonts/inter-latin-wght-normal.woff2
18be452724bfdc236c074ca94a249a7f41a86752c7d04ab258ce9ed5651f6a7e  fonts/jetbrains-mono-latin-wght-normal.woff2
9d9e26b0e8b3a68ef2e219a86504e0fbed673b76befd73cbca73013a48c70fbc  tokens.css
455cbdd7cde3a78c74ac331bef3b35b68b825f838d665333f454b01f820b6a4b  tokens.json
