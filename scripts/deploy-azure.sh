#!/usr/bin/env bash
set -euo pipefail

web_identity_id=$(azd env get-value WEBAPI_IDENTITY_ID)
web_identity_client_id=$(azd env get-value WEBAPI_IDENTITY_CLIENTID)
queue_endpoint=$(azd env get-value STORAGE_QUEUEENDPOINT)

: "${web_identity_id:?Missing WEBAPI_IDENTITY_ID; run azd provision first}"
: "${web_identity_client_id:?Missing WEBAPI_IDENTITY_CLIENTID; run azd provision first}"
: "${queue_endpoint:?Missing STORAGE_QUEUEENDPOINT; run azd provision first}"

storage_account_name=${queue_endpoint#https://}
storage_account_name=${storage_account_name%%.*}

export WEBAPI_IDENTITY_ID="${web_identity_id}"
export WEBAPI_IDENTITY_CLIENTID="${web_identity_client_id}"
export STORAGE_ACCOUNT_NAME="${storage_account_name}"

azd deploy "$@"
