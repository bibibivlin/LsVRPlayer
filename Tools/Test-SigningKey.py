"""Check keystore integrity and private-key decryption without disclosing passwords."""
import base64
import hashlib
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tempfile


def main():
    required = ("ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASS", "ANDROID_KEYALIAS_NAME", "ANDROID_KEYALIAS_PASS")
    for name in required:
        if not os.environ.get(name):
            raise RuntimeError("Missing required GitHub Secret: " + name)
    home = os.environ.get("JAVA_HOME")
    keytool = str(Path(home) / "bin" / ("keytool.exe" if os.name == "nt" else "keytool")) if home else shutil.which("keytool")
    if not keytool:
        raise RuntimeError("Java keytool is required")
    with tempfile.TemporaryDirectory(prefix="vrplayer-key-check-") as directory:
        root = Path(directory)
        key = root / "source.p12"
        key.write_bytes(base64.b64decode(os.environ["ANDROID_KEYSTORE_BASE64"].strip(), validate=True))
        certificate = root / "certificate.der"
        result = subprocess.run([keytool, "-exportcert", "-keystore", str(key), "-alias", os.environ["ANDROID_KEYALIAS_NAME"],
                                 "-storepass:env", "ANDROID_KEYSTORE_PASS", "-file", str(certificate)],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if result.returncode:
            raise RuntimeError("Keystore verification failed: check the keystore password and alias")
        print("Keystore password and certificate entry: verified", flush=True)
        environment = dict(os.environ, VRPLAYER_TEMP_KEY_PASSWORD=secrets.token_urlsafe(32))
        result = subprocess.run([keytool, "-importkeystore", "-noprompt", "-srckeystore", str(key),
                                 "-srcalias", os.environ["ANDROID_KEYALIAS_NAME"], "-srcstorepass:env", "ANDROID_KEYSTORE_PASS",
                                 "-srckeypass:env", "ANDROID_KEYALIAS_PASS", "-destkeystore", str(root / "check.p12"),
                                 "-deststoretype", "PKCS12", "-deststorepass:env", "VRPLAYER_TEMP_KEY_PASSWORD",
                                 "-destkeypass:env", "VRPLAYER_TEMP_KEY_PASSWORD"],
                                env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if result.returncode:
            raise RuntimeError("Private key decryption failed: check the alias private-key password")
        print("Private key password and decryption: verified")
        print("Release certificate SHA-256: " + hashlib.sha256(certificate.read_bytes()).hexdigest())


if __name__ == "__main__":
    main()
