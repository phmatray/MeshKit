#!/usr/bin/env bash
# Shrinks a pack's free in-browser previews (public/preview/*.glb): textures capped at 1024² and
# re-encoded as WebP. Geometry is untouched and the browser needs no decoder (EXT_texture_webp is
# native in three.js / <model-viewer>). Measured: 7.66 MB → 0.37 MB on a 2k-PBR prop.
#
# Never touches private/ (the paid files). Skips previews already in WebP: re-encoding would be a
# second lossy pass. Costs 0 Meshy credits, so it can be re-run on any pack at any time.
#
#   tools/optimize-previews.sh catalog/<slug> [max-texture-size]
set -euo pipefail

pack_dir="${1:?usage: tools/optimize-previews.sh <catalog>/<slug> [max-texture-size]}"
max_size="${2:-1024}"
cli="@gltf-transform/cli@4.4.2"   # pinned: its sharp/webp defaults decide what the preview looks like

preview_dir="${pack_dir}/public/preview"
if [ ! -d "${preview_dir}" ]; then
  echo "optimize-previews: no ${preview_dir}, nothing to do"
  exit 0
fi

shopt -s nullglob
total_before=0; total_after=0; done_count=0; skipped=0
for glb in "${preview_dir}"/*.glb; do
  before=$(wc -c < "${glb}")
  # Already WebP, or an untextured clay preview (geometry only — nothing this script changes).
  if grep -aq EXT_texture_webp "${glb}" || ! grep -aq '"images"' "${glb}"; then
    skipped=$((skipped + 1))
    total_before=$((total_before + before)); total_after=$((total_after + before))
    continue
  fi

  tmp="${glb%.glb}.optimizing.glb"   # must end in .glb: gltf-transform picks the output format from the extension
  npx -y "${cli}" optimize "${glb}" "${tmp}" \
    --texture-compress webp --texture-size "${max_size}" --compress false --simplify false >/dev/null
  after=$(wc -c < "${tmp}")
  # A valid GLB starts with "glTF"; keep the original if anything looks wrong or got bigger.
  if [ "$(head -c 4 "${tmp}")" = "glTF" ] && [ "${after}" -lt "${before}" ]; then
    mv -f "${tmp}" "${glb}"
    done_count=$((done_count + 1))
  else
    echo "optimize-previews: keeping original $(basename "${glb}") (${before} → ${after} bytes)"
    rm -f "${tmp}"; after=${before}
  fi
  printf '  %-40s %7.2f MB → %6.2f MB\n' "$(basename "${glb}")" "$(echo "${before}/1048576" | bc -l)" "$(echo "${after}/1048576" | bc -l)"
  total_before=$((total_before + before)); total_after=$((total_after + after))
done

printf 'optimize-previews: %d optimised, %d already WebP · %.1f MB → %.1f MB\n' \
  "${done_count}" "${skipped}" "$(echo "${total_before}/1048576" | bc -l)" "$(echo "${total_after}/1048576" | bc -l)"
