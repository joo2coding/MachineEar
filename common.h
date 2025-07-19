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


#define BUF_SIZE 4096
#define PORT 9000

using json = nlohmann::json;      // ���ǻ� json Ÿ�� ����
using namespace std;


extern MYSQL* g_db; // ���� DB �ڵ�

struct WorkItem 
{
	SOCKET socket;                    // Ŭ���̾�Ʈ ����
	string protocol;                   // �������� ���ڿ�
    json json_conv;                    // JSON ������
    vector<unsigned char> payload;     // ����/���� ������
};

struct ConnInfo  // �������� ����ü
{
	string ip;                 // Ŭ���̾�Ʈ IP �ּ�
	SOCKET socket;            // Ŭ���̾�Ʈ ����
	string mac;              // MAC �ּ�	
	int client_id;          // Ŭ���̾�Ʈ ID (0=����ũ, 1=������, 2= AI)
	
};


WorkItem protocol_recv(WorkItem& item);

#endif