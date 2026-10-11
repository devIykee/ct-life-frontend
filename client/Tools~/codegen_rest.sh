#!/usr/bin/env bash
# REST model codegen for the Unity client from the backend developer's contract (source of truth for REST).
#   client/Tools~/codegen_rest.sh [path/to/openapi.(json|yaml)]
# Default spec: contract/openapi.yaml (backend contract-v1.0.0-rc1, read-only copy; see contract/SOURCE.md).
# Generator: openapi-generator-cli 7.25.0 from Maven Central (sha1 56a9bb79e3bb565f477eddca2c6daa288a9c6f35), Java 17.
# Models only (contract.md section 1); FE writes the HTTP layer. validatable=false because Unity's .NET Standard 2.1
# profile has no System.ComponentModel.DataAnnotations. patch_rest_codegen.py then fixes known generator bugs.
set -euo pipefail
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
SPEC="${1:-$REPO/contract/openapi.yaml}"
JAR="${CTLIFE_OAG_JAR:-$HOME/.cache/ctlife-codegen/oag-7.25.0.jar}"
OUT="$REPO/client/Assets/Generated/Rest"
if [ ! -f "$JAR" ]; then
  mkdir -p "$(dirname "$JAR")"
  curl -sSfL -o "$JAR" https://repo1.maven.org/maven2/org/openapitools/openapi-generator-cli/7.25.0/openapi-generator-cli-7.25.0.jar
fi
echo "56a9bb79e3bb565f477eddca2c6daa288a9c6f35  $JAR" | shasum -c - >/dev/null
rm -rf "$OUT"
# --skip-validate-spec: backend main eeef49d has one stray `description` under a media type
# (paths./payments/intents/{id}.get.responses.200.content.application/json). The parser ignores it, so the models
# are unaffected; reported upstream rather than edited in the read-only contract copy.
java -jar "$JAR" generate -i "$SPEC" -g csharp -o "$OUT" --skip-validate-spec \
  --global-property models,modelDocs=false,modelTests=false,apiTests=false,apiDocs=false,supportingFiles=false \
  --additional-properties=packageName=CTLife.Rest,targetFramework=netstandard2.1,nullableReferenceTypes=false,library=httpclient,validatable=false \
  2>&1 | grep -E "WARN|ERROR|Exception" | grep -v "OpenAPI 3.1 support is still in beta" || true
python3 "$REPO/client/Tools~/patch_rest_codegen.py"
echo "models: $(ls "$OUT/src/CTLife.Rest/Model" | wc -l | tr -d ' ') from $SPEC"
