import com.hierynomus.smbj.SMBClient;
import com.hierynomus.smbj.auth.AuthenticationContext;
import com.hierynomus.smbj.connection.Connection;
import com.hierynomus.smbj.session.Session;
import com.rapid7.client.dcerpc.transport.SMBTransportFactories;
import com.rapid7.client.dcerpc.mssrvs.ServerService;
import com.rapid7.client.dcerpc.mssrvs.dto.NetShareInfo1;

/** Read-only SRVSVC compatibility check. Uses anonymous authentication only. */
public final class SmbSharesSmokeTest {
    public static void main(String[] args) throws Exception {
        try (SMBClient client = new SMBClient();
             Connection connection = client.connect(args[0], Integer.parseInt(args[1]));
             Session session = connection.authenticate(AuthenticationContext.anonymous())) {
            ServerService service = new ServerService(SMBTransportFactories.SRVSVC.getTransport(session));
            for (NetShareInfo1 share : service.getShares1()) {
                System.out.println("Share: " + share.getNetName() + " (type " + share.getType() + ")");
            }
        }
    }
}
