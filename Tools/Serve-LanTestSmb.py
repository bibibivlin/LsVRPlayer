"""Explicitly scoped, temporary read-only LAN fixture for Android NSD/shared-folder tests.

Usage: python Tools/Serve-LanTestSmb.py --bind <this PC's LAN IPv4> --duration 600
Requires Impacket and zeroconf in Tools/.runtime. Exposes only generated TestMedia.
"""
import argparse
import os
from pathlib import Path
import socket
import sys
import threading
import time

sys.path.insert(0, str(Path(__file__).parent / '.runtime'))
from impacket import smbserver
from impacket import smb3structs as smb2
from zeroconf import Zeroconf, ServiceInfo

parser = argparse.ArgumentParser()
parser.add_argument('--bind', required=True)
parser.add_argument('--port', type=int, default=1445)
parser.add_argument('--duration', type=int, default=600)
parser.add_argument('--auth', action='store_true', help='Use fixture-only account vrplayer / fixture-test')
args = parser.parse_args()
if sys.platform == 'win32':
    original_open = os.open
    def open_binary(path, flags, mode=0o777, **kwargs):
        return original_open(path, flags | os.O_BINARY, mode, **kwargs)
    os.open = open_binary
root = Path(__file__).resolve().parent.parent / 'TestMedia'
fixture_root = root / 'LanFixtureShare'
fixture_root.mkdir(exist_ok=True)
for fixture in root.iterdir():
    if fixture.is_file() and fixture.suffix.lower() in {'.mp4', '.mkv', '.srt'} and fixture.name.startswith(('Baseline.', 'Stereo.', 'Audio.', 'Tracks.', 'Performance.')):
        target = fixture_root / fixture.name
        if not target.exists():
            os.link(fixture, target)
server = smbserver.SimpleSMBServer(listenAddress=args.bind, listenPort=args.port)
server.addShare('TestMedia', str(fixture_root), 'VRPlayer generated fixtures', readOnly='yes')
server.setSMB2Support(True)
server.setLogFile(str(root / 'lan-smb-test.log'))
server.setSMBChallenge('')
# Impacket's read-only check rejects FILE_OPEN_IF even for existing RPC pipes.
# Normalize only an already registered IPC pipe to FILE_OPEN; keep disk shares read-only.
def open_rpc_pipe(conn_id, smb_server, packet):
    request = smb2.SMB2Create(packet['Data'])
    share = smb_server.getConnectionData(conn_id)['ConnectedShares'].get(packet['TreeID'], {})
    name = smbserver.normalize_path(request['Buffer'][:request['NameLength']].decode('utf-16le'))
    if share.get('share type') == '3' and name in smb_server.getRegisteredNamedPipes() and request['CreateDisposition'] == smb2.FILE_OPEN_IF:
        request['CreateDisposition'] = smb2.FILE_OPEN
        packet['Data'] = request.getData()
    return smbserver.SMB2Commands.smb2Create(conn_id, smb_server, packet)
server.getServer().hookSmb2Command(smb2.SMB2_CREATE, open_rpc_pipe)
if args.auth:
    from impacket.ntlm import compute_lmhash, compute_nthash
    server.addCredential('vrplayer', 0, compute_lmhash('fixture-test'), compute_nthash('fixture-test'))
    config = server.getServer().getServerConfig()
    config.set('global', 'anonymous_logon', 'no')
    server.getServer().processConfigFile()
    def require_fixture_account(conn_id, smb_server, packet):
        identity = smb_server.getConnectionData(conn_id).get('AUTHENTICATE_MESSAGE')
        if identity is None or not identity['user_name']:
            return [smb2.SMB2Error()], None, smbserver.STATUS_ACCESS_DENIED
        return smbserver.SMB2Commands.smb2TreeConnect(conn_id, smb_server, packet)
    server.getServer().hookSmb2Command(smb2.SMB2_TREE_CONNECT, require_fixture_account)
zeroconf = Zeroconf(interfaces=[args.bind])
service = ServiceInfo('_smb._tcp.local.', f'VRPlayer Fixture {args.port}._smb._tcp.local.', addresses=[socket.inet_aton(args.bind)], port=args.port, server=f'vrplayer-fixture-{args.port}.local.')
threading.Thread(target=server.start, daemon=True).start()
try:
    zeroconf.register_service(service)
    print(f'Read-only LAN SMB fixture: {args.bind}:{args.port}; lifetime {args.duration}s', flush=True)
    time.sleep(args.duration)
finally:
    zeroconf.unregister_service(service)
    zeroconf.close()
    server.stop()
