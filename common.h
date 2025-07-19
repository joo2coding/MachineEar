#ifndef COMMOM_H_
#define COMMON_H_

#include <winsock2.h>             // 윈도우 소켓 프로그래밍 기본 헤더
#include <ws2tcpip.h>             // IP 주소 등 추가 소켓 함수 헤더
#include <windows.h>              // 윈도우 관련 함수 헤더
#include <iostream>               // 입출력 스트림 사용
#include <thread>                 // 스레드(동시처리) 기능 사용
#include <vector>                 // 동적 배열(스레드 관리용)
#include "json.hpp"               // JSON 라이브러리 헤더 파일
#pragma comment(lib, "ws2_32.lib") // 소켓 라이브러리 자동 연결

#pragma once
#include <mysql.h> // 또는 <mariadb/mysql.h>
#include <string>


#define BUF_SIZE 4096
#define PORT 9000

using json = nlohmann::json;      // 편의상 json 타입 정의
using namespace std;


extern MYSQL* g_db; // 전역 DB 핸들

struct WorkItem 
{
	SOCKET socket;                    // 클라이언트 소켓
	string protocol;                   // 프로토콜 문자열
    json json_conv;                    // JSON 데이터
    vector<unsigned char> payload;     // 파일/이진 데이터
};

struct ConnInfo  // 접속정보 구조체
{
	string ip;                 // 클라이언트 IP 주소
	SOCKET socket;            // 클라이언트 소켓
	string mac;              // MAC 주소	
	int client_id;          // 클라이언트 ID (0=마이크, 1=관리자, 2= AI)
	
};


WorkItem protocol_recv(WorkItem& item);

#endif