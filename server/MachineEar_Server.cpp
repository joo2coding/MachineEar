#include <iostream>
#include <WinSock2.h>
#include <Windows.h>
#include <thread>
#include <vector>
#include <string>
#include "json.hpp"

#pragma comment(lib, "ws2_32.lib")

#define PORT 9000
#define BUF_SIZE 4096

using json = nlohmann::json;
using namespace std;



// 클라이언트별 통신을 처리할 스레드 함수
void client_thread(SOCKET clientSock) {
    cout << "[*] 새 클라이언트 접속!" << endl;
    vector<unsigned char> buf_recv;

    while (true) {                                // 클라이언트와 계속 통신
        char buf[BUF_SIZE] = {0};                          // 수신 데이터 저장용 버퍼
        int len = recv(clientSock, buf, sizeof(buf), 0); // 데이터 수신
        buf_recv.insert(buf_recv.end(), buf, buf + len);
        if (buf_recv.size() < 8) continue;

        // 수신받은 스트림에서 헤더 분리
        uint32_t len_total, len_json;

        memcpy(&len_total, &buf_recv[0], sizeof(uint32_t));
        memcpy(&len_json, &buf_recv[4], sizeof(uint32_t));
        uint32_t len_file = len_total - len_json;

        if (buf_recv.size() < 8 + len_total) continue;

        // 수신받은 스트림에서 json과 file 데이터 분리
        string jsonStr;
        copy(buf_recv.begin() + 8, buf_recv.begin() + len_json, jsonStr);
        vector<unsigned char> payload(buf_recv.begin() + 8 + len_json, buf_recv.begin() + 8 + len_total);

        try {
            json recv_json = json::parse(std::string(buf, len)); // 받은 데이터 JSON 파싱
            std::string protocol = recv_json.value("Protocol", "");

            // 프로토콜 앞자리로 마이크/관리자 구분
            json send_json;
            //if (protocol.rfind("0-", 0) == 0)
            //    send_json = handle_mic_protocol(recv_json);      // 마이크용 처리
            //else if (protocol.rfind("1-", 0) == 0)
            //    send_json = handle_admin_protocol(recv_json);    // 관리자용 처리
            //else
            //    send_json["RESPONSE"] = "Unknown Client Type";   // 둘 다 아님

            //std::string sendStr = send_json.dump();              // 응답 JSON을 문자열로 변환
            //send(clientSock, sendStr.c_str(), sendStr.size(), 0); // 클라이언트로 전송

            // 이후에 받은 데이터는 유지
            size_t offset = 8 + len_total;
            buf_recv = vector<unsigned char>(buf_recv.begin() + offset, buf_recv.end());
 
        }
        catch (const std::exception& e) {                       // JSON 파싱 실패 등 예외
            cerr << "JSON 파싱 실패: " << e.what() << endl;
            const char* errMsg = "{\"result\": \"fail\", \"msg\": \"JSON parse error\"}";
            send(clientSock, errMsg, strlen(errMsg), 0);         // 에러 메시지 전송
            buf_recv.clear();
        }
    }

    closesocket(clientSock);                                     // 클라이언트 소켓 닫기
    cout << "[*] 클라이언트 연결 종료.\n";
}

// 메인 함수: 서버 진입점
int main() {
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
        cerr << "WSAStartup 실패" << endl;
        return 1;
    }

    SOCKET serverSock = socket(AF_INET, SOCK_STREAM, 0);
    if (serverSock == INVALID_SOCKET) {
        cerr << "소켓 생성 실패" << endl;
        WSACleanup();
        return 1;
    }

    sockaddr_in serverAddr = {};
    serverAddr.sin_family = AF_INET;
    serverAddr.sin_port = htons(PORT);
    serverAddr.sin_addr.s_addr = INADDR_ANY;
    if (::bind(serverSock, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
        cerr << "bind 실패" << endl;
        closesocket(serverSock);
        WSACleanup();
        return 1;
    }

    if (listen(serverSock, SOMAXCONN) == SOCKET_ERROR) {
        cerr << "listen 실패" << endl;
        closesocket(serverSock);
        WSACleanup();
        return 1;
    }

    cout << "서버 준비 완료. 여러 클라이언트 접속 대기 중...\n";

    vector<thread> threads;

    while (true) {
        SOCKET clientSock = accept(serverSock, nullptr, nullptr);
        if (clientSock == INVALID_SOCKET) {
            cerr << "accept 실패: " << WSAGetLastError() << endl;
            break;
        }

        threads.emplace_back(client_thread, clientSock);
        threads.back().detach();
    }

    closesocket(serverSock);
    WSACleanup();
    return 0;
}