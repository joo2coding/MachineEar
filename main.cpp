#include "common.h"   // 공용 구조체, 상수, 타입 등 선언된 헤더
#include <iomanip> // setw, se
#include <fcntl.h>     // _O_U8TEXT

vector<ConnInfo> list_conninfo;



// 수신
WorkItem recv_parse(SOCKET& clientSock, vector<unsigned char>& buf_recv)
{
    while (true)  // 이 클라이언트와 계속 통신을 반복
    {
        char buf[BUF_SIZE] = { 0 };   // 이번에 새로 받은 데이터를 임시 저장할 버퍼

        int len = recv(clientSock, buf, sizeof(buf), 0); // 데이터 수신

        if (len <= 0) // 수신 실패 또는 연결 종료
        {
            cerr << "[Error] 클라이언트 연결 종료 또는 수신 오류" << endl;
            return {}; // 빈 WorkItem 반환
        }

        buf_recv.insert(buf_recv.end(), buf, buf + len); // 버퍼에 데이터 이어붙임

        // 데이터가 아직 8바이트(헤더) 미만이면 계속 대기(수신)
        if (buf_recv.size() < 8) continue;

        uint32_t len_total, len_json; // 전체 데이터 길이, JSON 길이

        // [헤더] 앞 4바이트: 전체 데이터 길이(네트워크 바이트 오더)
        memcpy(&len_total, &buf_recv[0], sizeof(uint32_t));
        len_total = ntohl(len_total); // 빅엔디안->리틀엔디안 변환

        // [헤더] 그 다음 4바이트: JSON 데이터 길이(네트워크 바이트 오더)
        memcpy(&len_json, &buf_recv[4], sizeof(uint32_t));
        len_json = ntohl(len_json);



        // 파일(바이너리) 데이터 길이 계산 (전체-JSON)
        uint32_t len_file = len_total - len_json;

        // 전체 데이터가 아직 모두 안 들어왔으면 더 받기
        if (buf_recv.size() < 8 + len_total) continue;

        // [본문] JSON 문자열 추출 (헤더 8바이트 다음부터 JSON 길이만큼)
        string jsonStr(buf_recv.begin() + 8, buf_recv.begin() + 8 + len_json);

        // [본문] 파일 데이터 추출 (JSON 다음부터 파일 길이만큼)
        vector<unsigned char> payload(buf_recv.begin() + 8 + len_json, buf_recv.begin() + 8 + len_total);


        // 버퍼에서 방금 처리한 부분(헤더+본문) 삭제
        size_t offeset = 8 + len_total;
        buf_recv = vector<unsigned char>(buf_recv.begin() + offeset, buf_recv.end());
        

        try
        {
            // 수신 JSON을 파싱해서 구조화
            json recv_json = json::parse(jsonStr);
            string protocol = recv_json["PROTOCOL"]; // 프로토콜 값 추출

            wstring ws = Utf8ToUtf16(jsonStr);
            //cout << "[Recv] Protocol : " << protocol << endl;
            //cout << endl;

            //cout << "[Recv] :" << jsonStr << endl;
           
            wcout << L"[ Recv from " << clientSock << " ] Recv Message : " << ws << endl;
            cout << "-----------------------------------------------------------------------------------------" << endl;
          


            return WorkItem({ clientSock, protocol, recv_json, payload });
        }
        catch (const exception& e)   // JSON 파싱 등 예외 발생 시
        {
            cerr << "JSON 파싱 실패: " << e.what() << endl;
            // 필요한 경우 에러 메시지 송신 가능
        }
    }
}

// 송신
void send_workitem(WorkItem& item)
{
    item.json_conv["PROTOCOL"] = item.protocol; // 응답 JSON에 프로토콜 추가

    string sendStr = item.json_conv.dump(); // JSON 객체를 문자열로 변환
    uint32_t len_json = (uint32_t)sendStr.size();         // 응답 JSON의 길이
    uint32_t len_total = 0;
    if (!item.payload.empty())             // 파일 데이터가 있으면
    {
        len_total = len_json + item.payload.size();
    }
    else                                        // 파일 없으면 JSON 길이만
    {
        len_total = len_json;
    }


    uint32_t net_len_total = htonl(len_total);   // 네트워크 바이트 순서 변환
    uint32_t net_len_json = htonl(len_json);

    // 송신 버퍼 준비: [헤더(8)] + [JSON] + [파일데이터]
    vector<char> sendBuf;

    // 헤더(전체길이 4바이트)
    char* ptr_total = reinterpret_cast<char*>(&net_len_total);
    sendBuf.insert(sendBuf.end(), ptr_total, ptr_total + 4);

    // 헤더(JSON 길이 4바이트)
    char* ptr_json = reinterpret_cast<char*>(&net_len_json);
    sendBuf.insert(sendBuf.end(), ptr_json, ptr_json + 4);

    // JSON 문자열 추가(UTF-8)
    sendBuf.insert(sendBuf.end(), sendStr.begin(), sendStr.end());



    // 파일 데이터(있으면) 추가
    if (!item.payload.empty())
    {
        sendBuf.insert(sendBuf.end(), item.payload.begin(), item.payload.end());
    }   

    // 클라이언트에게 데이터 송신
    send(item.socket, sendBuf.data(), sendBuf.size(), 0);


  
    // UTF-8 → UTF-16 변환
    wstring ws = Utf8ToUtf16(sendStr);


    wcout << L"[ Send to " << item.socket << " ] Send Message :\n" << ws << endl;
    cout << "-----------------------------------------------------------------------------------------" << endl;

    /*wstring ws = Utf8ToUtf16(sendStr);
 
	wcout << L"[Send] Send Message : " << ws << endl;
    cout << "-----------------------------------------------------------------------------------------" << endl;
*/

}

// 클라이언트별 스레드
void client_thread(SOCKET clientSock)
{
    cout << "-----------------------------------------------------" << endl;
    cout << "[*] 새 클라이언트 접속! : " << clientSock << endl;
    vector<unsigned char> buf_recv; // 여러 번 받은 데이터를 누적 저장할 버퍼

    ConnInfo new_conn;
    new_conn.socket = clientSock;
    new_conn.client_id = -1;

    list_conninfo.push_back(new_conn);

    while (true)
    {
		// 클라이언트로부터 데이터 수신
        WorkItem recv_item = recv_parse(clientSock, buf_recv);
        if (recv_item.protocol.empty()) // 수신 실패 또는 연결 종료
        {
            cerr << "[Error] 클라이언트 연결 종료 또는 수신 오류" << endl;
            break; // 루프 종료
		}
    
        // 프로토콜별로 처리 (proc_protocol.cpp의 protocol_recv 함수 호출)
        WorkItem send_item = protocol_recv(recv_item);

        // 3. 송신
        send_workitem(send_item);
    }
    for (int i = 0; i < list_conninfo.size(); i++)
    {
        if ((list_conninfo[i].socket == clientSock) && (list_conninfo[i].client_id == 0))
        {
            Log_connect(list_conninfo[i].num_pin, false);

			WorkItem send_item; // 응답용 WorkItem 생성
            // 클라이언트(관리자)에게 상태 송신
            send_item.protocol = "1-1-0";
            for (int i = 0; i < list_conninfo.size(); i++)
            {
                if (list_conninfo[i].client_id == 1)
                {
                    send_item.socket = list_conninfo[i].socket; // 해당 마이크 소켓으로 설정
                    send_item = protocol_recv(send_item);
                    send_workitem(send_item); // 응답 전송
                    break;
                }
            }
            break;
        }
    }
    
    closesocket(clientSock); // 통신이 끝나면 소켓 닫기

    cout << "-----------------------------------------------------------------------------------------" << endl;
    cout << "[*] 클라이언트 연결 종료.\n";
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
    cout << "현재 접속 리스트" << endl;
    cout << endl;
    for (ConnInfo conn : list_conninfo) 
    {
        cout << "종류 : " << conn.client_id << " - 소켓 : " << conn.socket << endl;
    }

    cout << endl;
}


// 서버 메인 함수
int main()
{
    SetConsoleOutputCP(CP_UTF8);          // 필수!
    std::locale::global(std::locale(""));
    // Winsock 라이브러리 초기화
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0)
    {
        cerr << "WSAStartup 실패" << endl;
        return 1;
    }

    // 서버 소켓 생성 (IPv4, TCP)
    SOCKET serverSock = socket(AF_INET, SOCK_STREAM, 0);
    if (serverSock == INVALID_SOCKET)
    {
        cerr << "소켓 생성 실패" << endl;
        WSACleanup();
        return 1;
    }

    // 서버 주소 구조체 설정 (모든 IP, 지정 포트)
    sockaddr_in serverAddr = {};
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_port = htons(PORT);             // 상수 PORT 사용
    serverAddr.sin_addr.s_addr = INADDR_ANY;       // 모든 네트워크 인터페이스

    // 소켓에 주소 바인드
    if (::bind(serverSock, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR)
    {
        cerr << "bind 실패" << endl;
        closesocket(serverSock);
        WSACleanup();
        return 1;
    }

    // 클라이언트 연결 대기 상태 전환
    if (listen(serverSock, SOMAXCONN) == SOCKET_ERROR)
    {
        cerr << "listen 실패" << endl;
        closesocket(serverSock);
        WSACleanup();
        return 1;
    }

    cout << "서버 준비 완료. 여러 클라이언트 접속 대기 중...\n";

    vector<thread> threads; // 각 클라이언트용 스레드 관리

    // 무한루프: 클라이언트가 들어올 때마다 새 스레드로 처리
    while (true)
    {
        SOCKET clientSock = accept(serverSock, nullptr, nullptr); // 새 클라이언트 접속 수락
        if (clientSock == INVALID_SOCKET)
        {
            cerr << "accept 실패: " << WSAGetLastError() << endl;
            break;
        }

        threads.emplace_back(client_thread, clientSock); // 새 스레드 생성
        threads.back().detach(); // 백그라운드로 분리(자원 자동 정리)
    }

    closesocket(serverSock); // 서버 소켓 닫기
    WSACleanup();           // Winsock 정리

    return 0;
}
