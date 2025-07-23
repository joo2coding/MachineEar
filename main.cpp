#include "common.h"   // ���� ����ü, ���, Ÿ�� �� ����� ���
#include <iomanip> // setw, se
#include <fcntl.h>     // _O_U8TEXT

vector<ConnInfo> list_conninfo;



// ����
WorkItem recv_parse(SOCKET& clientSock, vector<unsigned char>& buf_recv)
{
    while (true)  // �� Ŭ���̾�Ʈ�� ��� ����� �ݺ�
    {
        char buf[BUF_SIZE] = { 0 };   // �̹��� ���� ���� �����͸� �ӽ� ������ ����

        int len = recv(clientSock, buf, sizeof(buf), 0); // ������ ����

        if (len <= 0) // ���� ���� �Ǵ� ���� ����
        {
            cerr << "[Error] Ŭ���̾�Ʈ ���� ���� �Ǵ� ���� ����" << endl;
            return {}; // �� WorkItem ��ȯ
        }

        buf_recv.insert(buf_recv.end(), buf, buf + len); // ���ۿ� ������ �̾����

        // �����Ͱ� ���� 8����Ʈ(���) �̸��̸� ��� ���(����)
        if (buf_recv.size() < 8) continue;

        uint32_t len_total, len_json; // ��ü ������ ����, JSON ����

        // [���] �� 4����Ʈ: ��ü ������ ����(��Ʈ��ũ ����Ʈ ����)
        memcpy(&len_total, &buf_recv[0], sizeof(uint32_t));
        len_total = ntohl(len_total); // �򿣵��->��Ʋ����� ��ȯ

        // [���] �� ���� 4����Ʈ: JSON ������ ����(��Ʈ��ũ ����Ʈ ����)
        memcpy(&len_json, &buf_recv[4], sizeof(uint32_t));
        len_json = ntohl(len_json);



        // ����(���̳ʸ�) ������ ���� ��� (��ü-JSON)
        uint32_t len_file = len_total - len_json;

        // ��ü �����Ͱ� ���� ��� �� �������� �� �ޱ�
        if (buf_recv.size() < 8 + len_total) continue;

        // [����] JSON ���ڿ� ���� (��� 8����Ʈ �������� JSON ���̸�ŭ)
        string jsonStr(buf_recv.begin() + 8, buf_recv.begin() + 8 + len_json);

        // [����] ���� ������ ���� (JSON �������� ���� ���̸�ŭ)
        vector<unsigned char> payload(buf_recv.begin() + 8 + len_json, buf_recv.begin() + 8 + len_total);


        // ���ۿ��� ��� ó���� �κ�(���+����) ����
        size_t offeset = 8 + len_total;
        buf_recv = vector<unsigned char>(buf_recv.begin() + offeset, buf_recv.end());
        

        try
        {
            // ���� JSON�� �Ľ��ؼ� ����ȭ
            json recv_json = json::parse(jsonStr);
            string protocol = recv_json["PROTOCOL"]; // �������� �� ����

            wstring ws = Utf8ToUtf16(jsonStr);
            //cout << "[Recv] Protocol : " << protocol << endl;
            //cout << endl;

            //cout << "[Recv] :" << jsonStr << endl;
           
            wcout << L"[ Recv from " << clientSock << " ] Recv Message : " << ws << endl;
            cout << "-----------------------------------------------------------------------------------------" << endl;
          


            return WorkItem({ clientSock, protocol, recv_json, payload });
        }
        catch (const exception& e)   // JSON �Ľ� �� ���� �߻� ��
        {
            cerr << "JSON �Ľ� ����: " << e.what() << endl;
            // �ʿ��� ��� ���� �޽��� �۽� ����
        }
    }
}

// �۽�
void send_workitem(WorkItem& item)
{
    item.json_conv["PROTOCOL"] = item.protocol; // ���� JSON�� �������� �߰�

    string sendStr = item.json_conv.dump(); // JSON ��ü�� ���ڿ��� ��ȯ
    uint32_t len_json = (uint32_t)sendStr.size();         // ���� JSON�� ����
    uint32_t len_total = 0;
    if (!item.payload.empty())             // ���� �����Ͱ� ������
    {
        len_total = len_json + item.payload.size();
    }
    else                                        // ���� ������ JSON ���̸�
    {
        len_total = len_json;
    }


    uint32_t net_len_total = htonl(len_total);   // ��Ʈ��ũ ����Ʈ ���� ��ȯ
    uint32_t net_len_json = htonl(len_json);

    // �۽� ���� �غ�: [���(8)] + [JSON] + [���ϵ�����]
    vector<char> sendBuf;

    // ���(��ü���� 4����Ʈ)
    char* ptr_total = reinterpret_cast<char*>(&net_len_total);
    sendBuf.insert(sendBuf.end(), ptr_total, ptr_total + 4);

    // ���(JSON ���� 4����Ʈ)
    char* ptr_json = reinterpret_cast<char*>(&net_len_json);
    sendBuf.insert(sendBuf.end(), ptr_json, ptr_json + 4);

    // JSON ���ڿ� �߰�(UTF-8)
    sendBuf.insert(sendBuf.end(), sendStr.begin(), sendStr.end());



    // ���� ������(������) �߰�
    if (!item.payload.empty())
    {
        sendBuf.insert(sendBuf.end(), item.payload.begin(), item.payload.end());
    }   

    // Ŭ���̾�Ʈ���� ������ �۽�
    send(item.socket, sendBuf.data(), sendBuf.size(), 0);


  
    // UTF-8 �� UTF-16 ��ȯ
    wstring ws = Utf8ToUtf16(sendStr);


    wcout << L"[ Send to " << item.socket << " ] Send Message :\n" << ws << endl;
    cout << "-----------------------------------------------------------------------------------------" << endl;

    /*wstring ws = Utf8ToUtf16(sendStr);
 
	wcout << L"[Send] Send Message : " << ws << endl;
    cout << "-----------------------------------------------------------------------------------------" << endl;
*/

}

// Ŭ���̾�Ʈ�� ������
void client_thread(SOCKET clientSock)
{
    cout << "-----------------------------------------------------" << endl;
    cout << "[*] �� Ŭ���̾�Ʈ ����! : " << clientSock << endl;
    vector<unsigned char> buf_recv; // ���� �� ���� �����͸� ���� ������ ����

    ConnInfo new_conn;
    new_conn.socket = clientSock;
    new_conn.client_id = -1;

    list_conninfo.push_back(new_conn);

    while (true)
    {
		// Ŭ���̾�Ʈ�κ��� ������ ����
        WorkItem recv_item = recv_parse(clientSock, buf_recv);
        if (recv_item.protocol.empty()) // ���� ���� �Ǵ� ���� ����
        {
            cerr << "[Error] Ŭ���̾�Ʈ ���� ���� �Ǵ� ���� ����" << endl;
            break; // ���� ����
		}
    
        // �������ݺ��� ó�� (proc_protocol.cpp�� protocol_recv �Լ� ȣ��)
        WorkItem send_item = protocol_recv(recv_item);

        // 3. �۽�
        send_workitem(send_item);
    }
    for (int i = 0; i < list_conninfo.size(); i++)
    {
        if ((list_conninfo[i].socket == clientSock) && (list_conninfo[i].client_id == 0))
        {
            Log_connect(list_conninfo[i].num_pin, false);

			WorkItem send_item; // ����� WorkItem ����
            // Ŭ���̾�Ʈ(������)���� ���� �۽�
            send_item.protocol = "1-1-0";
            for (int i = 0; i < list_conninfo.size(); i++)
            {
                if (list_conninfo[i].client_id == 1)
                {
                    send_item.socket = list_conninfo[i].socket; // �ش� ����ũ �������� ����
                    send_item = protocol_recv(send_item);
                    send_workitem(send_item); // ���� ����
                    break;
                }
            }
            break;
        }
    }
    
    closesocket(clientSock); // ����� ������ ���� �ݱ�

    cout << "-----------------------------------------------------------------------------------------" << endl;
    cout << "[*] Ŭ���̾�Ʈ ���� ����.\n";
    cout << endl;
    for (int i = 0; i < list_conninfo.size(); i++) {
        if (list_conninfo[i].socket == clientSock) {
            list_conninfo.erase(list_conninfo.begin() + i);
            break;
        }
    }
}

void refresh_conninfo() 
{
    cout << endl;
    cout << "���� ���� ����Ʈ" << endl;
    cout << endl;
    for (ConnInfo conn : list_conninfo) 
    {
        cout << "���� : " << conn.client_id << " - ���� : " << conn.socket << endl;
    }

    cout << endl;
}


// ���� ���� �Լ�
int main()
{
    SetConsoleOutputCP(CP_UTF8);          // �ʼ�!
    std::locale::global(std::locale(""));
    // Winsock ���̺귯�� �ʱ�ȭ
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0)
    {
        cerr << "WSAStartup ����" << endl;
        return 1;
    }

    // ���� ���� ���� (IPv4, TCP)
    SOCKET serverSock = socket(AF_INET, SOCK_STREAM, 0);
    if (serverSock == INVALID_SOCKET)
    {
        cerr << "���� ���� ����" << endl;
        WSACleanup();
        return 1;
    }

    // ���� �ּ� ����ü ���� (��� IP, ���� ��Ʈ)
    sockaddr_in serverAddr = {};
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_port = htons(PORT);             // ��� PORT ���
    serverAddr.sin_addr.s_addr = INADDR_ANY;       // ��� ��Ʈ��ũ �������̽�

    // ���Ͽ� �ּ� ���ε�
    if (::bind(serverSock, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR)
    {
        cerr << "bind ����" << endl;
        closesocket(serverSock);
        WSACleanup();
        return 1;
    }

    // Ŭ���̾�Ʈ ���� ��� ���� ��ȯ
    if (listen(serverSock, SOMAXCONN) == SOCKET_ERROR)
    {
        cerr << "listen ����" << endl;
        closesocket(serverSock);
        WSACleanup();
        return 1;
    }

    cout << "���� �غ� �Ϸ�. ���� Ŭ���̾�Ʈ ���� ��� ��...\n";

    vector<thread> threads; // �� Ŭ���̾�Ʈ�� ������ ����

    // ���ѷ���: Ŭ���̾�Ʈ�� ���� ������ �� ������� ó��
    while (true)
    {
        SOCKET clientSock = accept(serverSock, nullptr, nullptr); // �� Ŭ���̾�Ʈ ���� ����
        if (clientSock == INVALID_SOCKET)
        {
            cerr << "accept ����: " << WSAGetLastError() << endl;
            break;
        }

        threads.emplace_back(client_thread, clientSock); // �� ������ ����
        threads.back().detach(); // ��׶���� �и�(�ڿ� �ڵ� ����)
    }

    closesocket(serverSock); // ���� ���� �ݱ�
    WSACleanup();           // Winsock ����

    return 0;
}
