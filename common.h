#ifndef COMMOM_H_
#define COMMON_H_

#include <winsock2.h>             // ������ ���� ���α׷��� �⺻ ���
#include <ws2tcpip.h>             // IP �ּ� �� �߰� ���� �Լ� ���
#include <windows.h>              // ������ ���� �Լ� ���
#include <iostream>               // ����� ��Ʈ�� ���
#include <thread>                 // ������(����ó��) ��� ���
#include <vector>                 // ���� �迭(������ ������)
#include "json.hpp"               // JSON ���̺귯�� ��� ����
#pragma comment(lib, "ws2_32.lib") // ���� ���̺귯�� �ڵ� ����

#pragma once
#include <mysql.h> // �Ǵ� <mariadb/mysql.h>
#include <string>
#include <mutex>

#define BUF_SIZE 4096
#define PORT 9000
#define INIT {}

using json = nlohmann::json;      // ���ǻ� json Ÿ�� ����
using namespace std;

//extern MYSQL* g_db; // ���� DB �ڵ�

MYSQL* connect_db();

struct WorkItem 
{
	SOCKET socket =INVALID_SOCKET;                    // Ŭ���̾�Ʈ ����
	string protocol;                   // �������� ���ڿ�
    json json_conv;                    // JSON ������
    vector<unsigned char> payload;     // ����/���� ������
};

struct ConnInfo  // �������� ����ü
{
	SOCKET socket = INVALID_SOCKET;            // Ŭ���̾�Ʈ ����
	string mac;
	int num_pin = -1;             
	int client_id = -1;          // Ŭ���̾�Ʈ ID (0=����ũ, 1=������, 2= AI)
};

extern vector<ConnInfo> list_conninfo;



WorkItem protocol_recv(WorkItem& item);
void refresh_conninfo();
void send_workitem(WorkItem& item);
void Log_connect(int num_pin, bool state_connect);
//WorkItem proc_1_2_0_recv(WorkItem& item);

std::wstring Utf8ToUtf16(const std::string& utf8);
inline string Utf16ToUtf8(const std::wstring& utf16);
string toUTF8_safely(const string& cp949Str);

#endif