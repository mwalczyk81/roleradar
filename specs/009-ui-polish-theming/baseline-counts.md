# Baseline counts (T001)

Recorded on branch `009-ui-polish-theming` at `317f1fc`, working tree clean apart from this spec folder. Numbers are from command output.

## App (`web/app`, excluding `web/app/ds`)

```text
$ grep -rnoE "style=\{" web/app --include=*.tsx --exclude-dir=ds | wc -l
203
$ grep -rlE "style=\{" web/app --include=*.tsx --exclude-dir=ds | wc -l
17
$ grep -rnoE "style=\{" web/app --include=*.ts --exclude-dir=ds | wc -l
0
```

Per file: `companies.$id.tsx` 40, `postings.$id.tsx` 37, `applications.$id.tsx` 32, `CreateApplicationDialog.tsx` 24, `TransitionPostingDialog.tsx` 17, `applications._index.tsx` 9, `notes.$id.tsx` 8, `notes._index.tsx` 6, `postings._index.tsx` 5, `criteria.tsx` 5, `auth.callback.tsx` 5, `companies._index.tsx` 4, `service-unavailable.tsx` 3, `search-runs.tsx` 3, `not-authorized.tsx` 2, `auth.logout.tsx` 2, `root.tsx` 1.

Matches the plan: 203 in 17 files.

## Kit (`web/app/ds/components/src`)

```text
$ grep -rnoE "style=\{" web/app/ds/components/src | wc -l
150
$ grep -rlE "style=\{" web/app/ds/components/src | wc -l
27
```

Matches the plan: 150 in 27 files.
