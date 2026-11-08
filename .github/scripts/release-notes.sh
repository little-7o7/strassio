#!/usr/bin/env bash
# Обновляет заголовок и описание выпусков в $RELEASES_REPO из docs/releases/vX.Y.Z.md (первая строка «# …» —
# заголовок, остальное — описание, как в build.yml). Запускается из .github/workflows/release-notes.yml.
#   INPUT_TAG — тег, заданный вручную; иначе BEFORE — коммит до push: берутся изменённые с тех пор файлы.
#   Изменился сам обработчик (этот файл или workflow) — обновляется последний выпуск.
set -euo pipefail

if [ -z "${GH_TOKEN:-}" ]; then
  echo "::warning::Нет секрета RELEASES_TOKEN — описание выпуска не обновить"
  exit 0
fi

SELF='^\.github/(workflows/release-notes\.yml|scripts/release-notes\.sh)$'
ZERO=0000000000000000000000000000000000000000
latest() { ls docs/releases/v*.md 2>/dev/null | sort -V | tail -1; }

if [ -n "${INPUT_TAG:-}" ]; then
  if ! [[ "$INPUT_TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
    echo "::error::Тег должен быть вида v1.2.3 или v1.2.3-beta.1"
    exit 1
  fi
  files="docs/releases/$INPUT_TAG.md"
elif [ -n "${BEFORE:-}" ] && [ "$BEFORE" != "$ZERO" ] && git cat-file -e "$BEFORE^{commit}" 2>/dev/null; then
  changed=$(git diff --name-only "$BEFORE" HEAD)
  if grep -Eq "$SELF" <<<"$changed"; then
    files=$(latest)
  else
    files=$(git diff --name-only --diff-filter=AM "$BEFORE" HEAD -- 'docs/releases/v*.md')
  fi
else
  files=$(latest)
fi

for f in $files; do
  if [ ! -f "$f" ]; then
    echo "::warning::Нет файла $f"
    continue
  fi
  tag=$(basename "$f" .md)
  if ! gh release view "$tag" --repo "$RELEASES_REPO" >/dev/null 2>&1; then
    echo "Выпуска $tag ещё нет — пропускаю: его создаст «Сборка установщика»"
    continue
  fi
  title=$(head -n 1 "$f" | sed -E 's/^#[[:space:]]*//')
  notes="${RUNNER_TEMP:-/tmp}/notes-$tag.md"
  tail -n +2 "$f" > "$notes"
  gh release edit "$tag" --repo "$RELEASES_REPO" --title "$title" --notes-file "$notes"
  echo "Описание обновлено: https://github.com/$RELEASES_REPO/releases/tag/$tag" | tee -a "${GITHUB_STEP_SUMMARY:-/dev/null}"
done
