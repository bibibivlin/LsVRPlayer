"""Local-only SMB2 fixture server. Reach it from the headset using adb reverse tcp:1445 tcp:1445."""
from pathlib import Path
import os
import sys
sys.path.insert(0, str(Path(__file__).parent / '.runtime'))
from impacket import smbserver

# Impacket's read-only share branch resets O_BINARY on Windows. Preserve binary
# mode so control-Z and CRLF bytes inside video files are not treated as text.
if sys.platform == 'win32':
    original_open = os.open
    def open_binary(path, flags, mode=0o777, **kwargs):
        return original_open(path, flags | os.O_BINARY, mode, **kwargs)
    os.open = open_binary

root = Path(__file__).resolve().parent.parent / 'TestMedia'
root.mkdir(exist_ok=True)
server = smbserver.SimpleSMBServer(listenAddress='127.0.0.1', listenPort=1445)
server.addShare('TestMedia', str(root), 'VRPlayer generated test fixtures', readOnly='yes')
server.setSMB2Support(True)
server.setLogFile(str(root / 'smb-test.log'))
server.setSMBChallenge('')
print('Read-only SMB test share at 127.0.0.1:1445/TestMedia', flush=True)
server.start()
