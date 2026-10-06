#!/usr/bin/env bash
set -euo pipefail

: "${ARCHIVA_API_BASE_URL:?Set ARCHIVA_API_BASE_URL to the API origin, for example https://api.example.com}"
: "${ARCHIVA_ACCESS_TOKEN:?Set ARCHIVA_ACCESS_TOKEN to an administrator access token}"

after_document_id=0
while true; do
  response=$(curl --fail-with-body --silent --show-error \
    --request POST \
    --url "${ARCHIVA_API_BASE_URL%/}/api/documents/analysis/backfill" \
    --header "Authorization: Bearer ${ARCHIVA_ACCESS_TOKEN}" \
    --header 'Content-Type: application/json' \
    --data "{\"afterDocumentId\":${after_document_id},\"batchSize\":100}")

  queued_count=$(jq -r '.queuedCount' <<<"${response}")
  next_after_document_id=$(jq -r '.nextAfterDocumentId // empty' <<<"${response}")
  printf 'Queued %s documents after ID %s\n' "${queued_count}" "${after_document_id}"

  if [[ -z "${next_after_document_id}" ]]; then
    break
  fi

  after_document_id=${next_after_document_id}
done
