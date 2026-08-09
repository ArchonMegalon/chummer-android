#!/usr/bin/env bash
set -euo pipefail

: "${CHUMMER_ANDROID_SIGNING_DIR:?Set CHUMMER_ANDROID_SIGNING_DIR to an absolute directory outside the repository}"

case "${CHUMMER_ANDROID_SIGNING_DIR}" in
  /*) ;;
  *)
    echo "CHUMMER_ANDROID_SIGNING_DIR must be an absolute path" >&2
    exit 2
    ;;
esac

chummer_android_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
chummer_signing_dir="${CHUMMER_ANDROID_SIGNING_DIR%/}"

case "${chummer_signing_dir}/" in
  "${chummer_android_root}/"*)
    echo "Signing material must be stored outside the Chummer Android repository" >&2
    exit 2
    ;;
esac

chummer_keystore_path="${chummer_signing_dir}/chummer-upload.p12"
chummer_certificate_path="${chummer_signing_dir}/chummer-upload-cert.pem"
chummer_environment_path="${chummer_signing_dir}/android-release.env"
chummer_key_alias="chummer-upload"
chummer_android_image="ghcr.io/cirruslabs/android-sdk@sha256:f9b3ea9ed2b5fc9522adae82c7b4622ab7aa54207ef532c8e615a347dca08f31"

for chummer_target in \
  "${chummer_keystore_path}" \
  "${chummer_certificate_path}" \
  "${chummer_environment_path}"
do
  if [[ -e "${chummer_target}" || -L "${chummer_target}" ]]; then
    echo "Refusing to replace existing signing material: ${chummer_target}" >&2
    exit 3
  fi
done

command -v openssl >/dev/null

umask 077
install -d -m 0700 "${chummer_signing_dir}"
chummer_store_password="$(openssl rand -hex 32)"
export CHUMMER_PROVISION_STORE_PASSWORD="${chummer_store_password}"

chummer_keytool() {
  if command -v keytool >/dev/null; then
    keytool "$@"
    return
  fi

  command -v docker >/dev/null
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e CHUMMER_PROVISION_STORE_PASSWORD \
    -v "${chummer_signing_dir}:${chummer_signing_dir}" \
    "${chummer_android_image}" \
    keytool "$@"
}

chummer_keytool -genkeypair \
  -keystore "${chummer_keystore_path}" \
  -storetype PKCS12 \
  -storepass:env CHUMMER_PROVISION_STORE_PASSWORD \
  -keypass:env CHUMMER_PROVISION_STORE_PASSWORD \
  -alias "${chummer_key_alias}" \
  -keyalg RSA \
  -keysize 4096 \
  -sigalg SHA256withRSA \
  -validity 9125 \
  -dname "CN=Chummer Upload, OU=Android Release, O=My External Brain, L=Vienna, C=AT" \
  -noprompt

chummer_keytool -exportcert \
  -rfc \
  -keystore "${chummer_keystore_path}" \
  -storetype PKCS12 \
  -storepass:env CHUMMER_PROVISION_STORE_PASSWORD \
  -alias "${chummer_key_alias}" \
  -file "${chummer_certificate_path}"

install -m 0600 /dev/null "${chummer_environment_path}"
printf '%s\n' \
  "AndroidSigningKeyStore=${chummer_keystore_path}" \
  "ChummerAndroidSigningStorePass=${chummer_store_password}" \
  "ChummerAndroidSigningKeyAlias=${chummer_key_alias}" \
  "ChummerAndroidSigningKeyPass=${chummer_store_password}" \
  "CHUMMER_ANDROID_UPLOAD_CERTIFICATE_PATH=${chummer_certificate_path}" \
  >"${chummer_environment_path}"

chmod 0600 \
  "${chummer_keystore_path}" \
  "${chummer_certificate_path}" \
  "${chummer_environment_path}"

unset CHUMMER_PROVISION_STORE_PASSWORD
unset chummer_store_password

printf 'upload_keystore=%s\n' "${chummer_keystore_path}"
printf 'public_certificate=%s\n' "${chummer_certificate_path}"
printf 'release_environment=%s\n' "${chummer_environment_path}"
printf 'key_alias=%s\n' "${chummer_key_alias}"
