# Design system sync

Vendored copy of Matt's Design System. The vendored copy under `web/app/ds` is strictly copy-only and NEVER edited by hand. Any fixes, styling updates, or Part C additions must be published to the upstream design kit artifact first, and then brought into this repository via a clean re-sync.

- Source: Design System artifact, <https://claude.ai/artifact/E2SU2aXqnLZ5r9tu5VE9Mm>
- Kit version: 1793983477-b1df (v27, no inline styles: every component styles itself through classes in bundle.css; v26 plus the K1 change)
- Synced: 2026-11-06
- Synced by: Claude (published to the kit artifact first, then copied unmodified; `project/` prefix removed)
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
| `fonts/*.woff2` | `project/fonts/*` | IBM Plex Sans (variable), IBM Plex Serif (400, 500, 600) and IBM Plex Mono (400, 500), referenced by `tokens.css`. |
| `README.md` | `project/README.md` | The kit's brand book. |

Not vendored: `bundle.js` (the preview runtime), `preview.html` cards, `ui_kits/`, `templates/`, `archived/`, and the font files `tokens.css` no longer references (the kit still holds Inter, JetBrains Mono and Fraunces; this copy does not).

## Status

Part C composites are in this sync: DataTable, Toolbar (`components/src/data/`), AppShell (`components/src/layout/`), EmptyState, Skeleton, Toast, ToastRegion (`components/src/feedback/`), Menu and Tooltip (`components/src/overlay/`). Use them; do not build local versions.
The kit also corrected the prop types of Alert, Input and Select and the stale `base.css` comments in Checkbox, Radio and Switch.
Verified before publishing: 88 jsdom behaviour checks (sorting, paging, keyboard, focus, timers), axe with no violations on a composed page, WCAG AA contrast for every new colour pair in both themes, and the declaration files type-check under strict TypeScript. Also checked by eye in a browser, in light and dark themes: DataTable, Toolbar, AppShell, EmptyState, Skeleton, Toast, Menu (keyboard and focus return) and Tooltip. That check found and fixed a clipped tooltip, a wrapping Badge and the AppShell mobile layout gap.

Chart components are in this sync (added for the insights dashboard): StatTile, BarChart (funnel mode), ColumnChart and Sparkline in `components/src/data/`, with `chartParts.jsx` beside them (shared internals, not a component, no `.d.ts`; import the four components, never `chartParts.jsx`). They read four tokens, `--ds-chart-primary`, `--ds-chart-primary-hover`, `--ds-chart-muted` and `--ds-chart-muted-hover`, defined in `tokens.json` and `tokens.css` (light and dark). The kit's published `tokens.css` had been missing those four tokens until kit version 23 added them, so a sync from an earlier version leaves the chart marks without a colour. `components/bundle.css` carries the chart hover, focus and forced-colours rules; `components/index.d.ts` exports the new props. Verified before publishing: 91 jsdom behaviour checks, axe clean in chart and table view, strict TypeScript on the declaration files, contrast pairs computed for both themes, and a real-browser pass in light and dark on the published preview cards.

Identity pass (kit versions 24 and 25): the accent is the bronze ramp from the owner's personal site (`--ds-bronze-50` to `--ds-bronze-900`, replacing blue for primary, focus, subtle, and chart tokens), the neutrals are warm stone, the dark theme is layered (page `neutral-900`, zones `neutral-850`, cards `neutral-800`, hover and borders `neutral-700`), and the warning tone is yellow. Type is IBM Plex: Plex Serif is `--ds-font-display` and carries the AppShell brand and title, `.ds-hero`, `.ds-page-title` and the StatTile figure; Plex Sans is the body face and Plex Mono the code and number face. The info Alert is a neutral surface, and the current AppShell link carries a 2px accent rule. `components/src/feedback/Alert.jsx` and `components/src/data/StatTile.jsx` changed in this sync; all other component sources are unchanged. Verified before publishing: 86 contrast pairs (text, muted text, status text on tinted grounds, control borders, focus ring and chart marks, in both themes), all passing; the kit's `bundle.js` was patched in place for the two component changes and the unchanged blocks were left byte-identical.

DataTable gained a `sortScope` prop in kit version 26: `"page"` (default, unchanged) says in the footer and the header tooltip that sorting applies to the rows on the page; `"all"` is for tables the API sorts, and removes both. `components/src/data/DataTable.jsx`, `components/DataTable/DataTable.d.ts`, `components/DataTable/README.md` and `components/index.d.ts` changed in that sync.

No inline styles (kit version 27, 009 K1): no component writes a `style` attribute into its markup, so a page can set
`style-src-attr 'none'`. Static styling is classes and `data-*` attributes in `components/bundle.css` (the original file is
unchanged at the top; K1 only appends rules and the `--ds-placeholder` token, so the personal site, which vendors only
`tokens.css` and `bundle.css`, keeps working). Chart sizes (bar lengths, column heights, tick positions) are whole percents
(`.ds-w-N`, `.ds-h-N`, `.ds-b-N`, `.ds-cap-N`, `.ds-bar-w-N`, generated at the end of the file), so server-rendered charts
paint without script; a ColumnChart `height` snaps to a multiple of 10px (60 to 400) through `[data-plot-h]`. Values only
known in the browser (the chart readout position, the Tooltip viewport shift, the Modal `width`, DataTable free-form column
widths and `maxHeight`, Skeleton free-form sizes) are set through the CSSOM, which the policy allows. New props: `Card`
`padding` (`none`, `sm`, `md`, `lg`) and `Input` `width` (`xs`, `sm`, `md`). `Input`, `Textarea` and the Toolbar search read
the new `--ds-placeholder` token (the muted text colour, 4.5:1 or better in both themes). `Sparkline` stays. The kit's own
`bundle.js` (the preview runtime, not vendored) was not rebuilt, so the preview cards still draw the old inline-styled
components. Verified here: 33 server-render cases with no style attribute (`tests/unit/kit-no-style-attributes.test.tsx`), and
the visual baseline unchanged on 64 of 74 captures; the 10 that changed are the Insights charts, by about a pixel where a mark is
rounded to a whole percent.

## Re-sync

Ask Claude to re-export from the kit. Update the version, date and hashes above, and review the diff like any other change.

## File hashes (sha256, as published)

3447e398a4abcda815c68a35d349ad89ec286d633268571b18277706f1455076  README.md
f03c3f7e1c89a87a5c67139f80ec1f8bdfa8aea67f98fcd7e6a7c28706927f3a  components/Alert/Alert.d.ts
d30c1553dec8a052133d702b544cc63ce611e45db838d114082ff8333affe815  components/Alert/README.md
a8648be0fcecaf3633c8b2952a22674f83882354954d1bbf43753f3c967fb164  components/AppShell/AppShell.d.ts
be5558c147c9bef7c246b5f8ce6880fc6debc58a2a6ebbfb6d6b11c136f2ce2c  components/AppShell/README.md
f9462f787019e2c3989bf9c85fd19df1c89d4a433ff6aedf2aae8c7db2a10eda  components/Avatar/Avatar.d.ts
08c995656433c0a4e9f4e115bd72d16a0313e8fe5dd4b6c5eb2190a77690423c  components/Avatar/README.md
55dfda0d05b69162e328958242c95a8f65a15f8fabdbb0b6472b0628d63b4c3e  components/Badge/Badge.d.ts
a8abf2d5e374af99d5e8402d40f2f449701aff213e5f148b62ae01fdd062d4f6  components/Badge/README.md
cc7e6aca97383646e60d33bee7fcef1657e86f0cb628208386565bb117ed6f06  components/BarChart/BarChart.d.ts
00cf8e759f68de0aa05eadd32e593d92ba0148b4118c2dd1e96494afa22148ea  components/BarChart/README.md
b3dec14da444c4535f24e2282c1806324c6ca17ce78c26df9ec173acd77943d2  components/Button/Button.d.ts
64e5d3612c7f7a255ae0fe1f7a19400e65ae803117969edab2473fcd65fc3bb1  components/Button/README.md
77cae53784dd3c277e72e2c0a05bc9a08082614e8abde7a7ecd72b254152317e  components/Card/Card.d.ts
0090a88cf377855a2e1b17eb54da151f438648b93e2781160ef4a016075e9132  components/Card/README.md
406a141f91cef020507b24a87760c899fba32304142d814682564a54e4a7addc  components/Checkbox/Checkbox.d.ts
c701fb944fe5d2c125a23141a4f3a096071e1a90c745d824106103cf7f5478cd  components/Checkbox/README.md
71300f01e5b41e33731ef9a7698ea250bd297fcf00fd8f8d04c47cdf5334fae9  components/ColumnChart/ColumnChart.d.ts
064d0c45d83c8c9b544d1f66e8c410210b7673fd8d7fd81e374eba1325e093b0  components/ColumnChart/README.md
b5a98ee63c76463363f1bb7e2a44d751148bfea0504b901f255f0003bcb0f7a9  components/DataTable/DataTable.d.ts
e7ec6295115fa3f66a6981ac36c1f7036bee4760800598868a625e10d61226d0  components/DataTable/README.md
aaca10374b6dbe0edf0b0cc74d76d3a5799be318b174baf2f610f659451757f5  components/EmptyState/EmptyState.d.ts
4eb942bde3c7572591203d543f2e3e341cf708eef84a1a564cd38e98534b7849  components/EmptyState/README.md
3354abe821fcab0132e64be29ef9e41c3bdddaab57e342288bf8b1d469f74d9e  components/Input/Input.d.ts
c9d76326868874f1915b04fc375245303180839b280d2bcc4475a935a5eb1d38  components/Input/README.md
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
62605b86ea95714de7fd6cbe324e4134d1bc45684f7632ac775729a4d8d7821e  components/Sparkline/README.md
6b5ab9e72a1032537d6de1d0e6b9be975edb44b2c39e0a04a56a818acd185040  components/Sparkline/Sparkline.d.ts
de3d833ee7be44c1214ee1679d070b893e9d2b32c4ebd1f8a500ad1664bfb864  components/StatTile/README.md
593c01aa0b67b4ceaca4bde75eac2ed3dc3037f7bf80200fdcbb0643bf9f2669  components/StatTile/StatTile.d.ts
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
3c80f9f708ad5ed80eb36eba93895ddf05a031362aa72cfad265fbab5ea7f9b2  components/bundle.css
e2cfaaa201607481ac37f47aeb9dc9e9725cd0ed4e56d72db3d0a8b8f62cb317  components/index.d.ts
f095e254a99c19d109cf3f7a3cad9f9a6c63dfe5fdd22d44529599486261181e  components/src/data/BarChart.jsx
847516f78f91553322ed4160a184dd12a55ab274ad56e6d784e8a711b79dbeb5  components/src/data/ColumnChart.jsx
4be81e6b34d7eec66fbe2f539288835a94ddee6b3abc48c9fdc2bb0eb6d8ebfc  components/src/data/DataTable.jsx
0d6c77496489e13d17e0eabf9bdc063c50c3c353af0e39b3c7bbf1b3f518ba2c  components/src/data/Sparkline.jsx
cc249498005f05575be55d749bc11a8df470c1c01a8bad8e80bdeb7cb5c1ec47  components/src/data/StatTile.jsx
34468111fb20e4242fd106d262bf9ed950b152674e214d8ea0ff2e2deaa50e8c  components/src/data/Toolbar.jsx
87b0a6dceec47fae701f263c445ee6b96b4f8426218474fd4752b15323aceb36  components/src/data/chartParts.jsx
a56e79e5ffba2125cbbae6b8d77d21724a03726a4a6f53d6b428982c9fd681cb  components/src/feedback/Alert.jsx
28a3f116b91e80257eb90d6f23f4320b87c2d58fc726da0104e676bebdfeab08  components/src/feedback/Avatar.jsx
cef9fa26924972ce1ad359e9fed081b9ac81d0b2eaaed1a41ab4f46f6f37b1c8  components/src/feedback/Badge.jsx
9d8e058c642499caa0b3e0a52810024872aa9d994493114400fd769495e35b2d  components/src/feedback/Card.jsx
21e09f80857c18cedec3cebb566556f588237c1af78378a7b532e4f1d911f11a  components/src/feedback/EmptyState.jsx
118211ffa9b784c11abbae42120b592392cb9e802845e397708edc9d52a058d6  components/src/feedback/Skeleton.jsx
b22d47409b1f6c23b918d94a76131035f8d13e7d1f5a29f322f7d7fbfd9f9985  components/src/feedback/Toast.jsx
f25b3558fb605ffaf153358cd8afbf4e21c4d7ac7127538c01dcec3f058bce5b  components/src/feedback/ToastRegion.jsx
a4ec0c2c14b19c33e219e083049334bed984c47e4004aa7bbbaf641b3ff44622  components/src/forms/Button.jsx
4b5c0c1d3be58b26e31088a3df8eb8581018d8f175c8d61f38807b82306bd729  components/src/forms/Checkbox.jsx
70bda513f12e6f981d488d5a366345004e880c40cf8f1056a4a8ed23bd377098  components/src/forms/Input.jsx
1c5194c8b17340473675bf5ff71a7f91e04e0ccf50bbbee679fc7d87d3738b4a  components/src/forms/Radio.jsx
54c02e805eceda2b70b993e1826ee74ea124ee23e37a115342115e6e0231295b  components/src/forms/Select.jsx
91759b8ff1f1aac453f4483ed336d4d3237a39e4b140ce65db78ce0734dc9595  components/src/forms/Switch.jsx
22d02a89c00af92072a3b11f1a0a7c31d375a7aba156c9b0111692cea1740930  components/src/forms/Textarea.jsx
0ba22a584a819812e12359cba2740565a76e6a96844e587dcc2916a911ce5340  components/src/layout/AppShell.jsx
89cc80036610681fba388d223b2e443458ef0cf2a9aa718c30600b2890a21de9  components/src/navigation/Tabs.jsx
84bd1b4f628cb68a56f651a84723dfea4e83a5fad1b7bcdd30fa255982e05686  components/src/overlay/Menu.jsx
3ba064c8f4cd2474dbf4b746df9a2a0609e3b5579b967ad6c8f02527e9f2d95b  components/src/overlay/Modal.jsx
bc27df7e3b809fd4d2e0d533dcffe0e0f02f7243be2d73cf7830e4b3c288fcf7  components/src/overlay/Tooltip.jsx
08949f728dc52d528e69b1667d15c89a5686a4ee9a296ff90983985f99c380f7  fonts/ibm-plex-mono-latin-400-normal.woff2
01d285447409c8a588692162439a038b8cbd7871309ee20267b0d2d91c6e8e22  fonts/ibm-plex-mono-latin-500-normal.woff2
e2291e842cf5af167122a22881a740c7f2dda7716f1e8cd76680264f4a859470  fonts/ibm-plex-sans-latin-wght-normal.woff2
cb2c5eee2c0a43ff30d2365407c7bc8b20e3bd90720a4a64102ba0b328022a02  fonts/ibm-plex-serif-latin-400-normal.woff2
677082fc2e9ee60701b874fe5983c98ea9fb3b588cc1b02058ff9bf29ea783e4  fonts/ibm-plex-serif-latin-500-normal.woff2
e279e4f8baa0d4634d98868092f2198ae0dcd9e142c935822c385b9843352a9b  fonts/ibm-plex-serif-latin-600-normal.woff2
cf40a7c05062ef842822e77c87356ebcfa771388c670fec506620682046bd31d  tokens.css
83f1d4ce2f8366c9e96f96db43e0a036412eef3dd6b199ce5f39b5612508e856  tokens.json
