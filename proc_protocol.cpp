#include "common.h"                    // ���� ����ü, Ÿ��, �Լ� ���� (WorkItem ��)
#include <fstream>                     // ���� ����� ��Ʈ�� ���

// [��������: ����ũ] 0-0-0 ó�� �Լ�
WorkItem proc_0_0_0(WorkItem& item) 
{
    WorkItem send_item;                // ����� WorkItem ��ü ����
    string protocol = item.protocol;   // ���� �������� �� ����

    send_item.protocol = protocol;     // ���� �������ݵ� �����ϰ� ����
    send_item.json_conv["RESPONSE"] = "OK"; // ���� JSON�� "OK" �޽���

    return send_item;                  // ���� ��ȯ
}

// [��������: ������] 1-0-0 ó�� �Լ� 
WorkItem proc_1_0_0(WorkItem& item) // �����ڰ� �������� ��, �ٸ� �����ڰ� �ִ��� Ȯ���ϰ� ����   
{
    WorkItem send_item;                // ����� WorkItem ����
    // �����δ� ������ �ߺ� ���� üũ �� ó��
    return send_item;                  // ����� �� ���� ��ȯ
}

// [��������: AI] 2-0-0 ó�� �Լ�
WorkItem proc_2_0_0(WorkItem& item) 
{
    WorkItem send_item;                // ����� WorkItem ����
    string protocol = item.protocol;   // �������� �� ����

    send_item.protocol = protocol;     // ���� �������� ����
    send_item.json_conv["RESPONSE"] = "OK"; // ���� JSON�� "OK" ����

    return send_item;                  // ���� ��ȯ
}

// [��������: AI] 2-1-0 ó�� �Լ� (������ AI���� �������� ����)
WorkItem proc_2_1_0(WorkItem& item) 
{
    WorkItem send_item;                        // ����� WorkItem ����
   
    send_item.protocol = item.protocol;             // ���� �������� ����

    // ���� ���
    string audio_path = "C:\\Users\\mjy\\Downloads\\2200031642_ToyConveyor_case2_normal_IND_ch3_1642 (1).wav";

    // ������ ���� ���� ����
    ifstream file(audio_path, ios::binary);

    uint32_t file_size_uint32;                 // ���� ũ�� ����� ����
    if (file.is_open()) 
    {
        // ������ ó������ ������ �о ���Ϳ� ����
        send_item.payload = vector<unsigned char>
        (
            istreambuf_iterator<char>(file),   // ���� ó������
            istreambuf_iterator<char>()        // ���� ������
        );
        streamsize file_size = file.tellg(); // ���� ��ġ(=���� ũ��) ���
        file.seekg(0, ios::end);                  // ���� �����͸� ó������ �̵�

        // ���� ũ�⸦ uint32_t�� ��ȯ�ؼ� ����
        file_size_uint32 = static_cast<uint32_t>(file_size);

        file.close();                         // ���� �ݱ�
    }
    else { cout << "���� ���� ����!" << endl; }    // ���� ���� ���� �� �޽��� ��� 
   
       
    // ���� ũ�� ���ϱ�
    ifstream file_size_stream(audio_path, ios::binary | std::ios::ate);

    cout << "proc_2_1_0 called" << endl;

    int file_size = 0;
    if (file_size_stream.is_open()) 
    {
        file_size = static_cast<int>(file_size_stream.tellg()); // ���� ũ�� int�� ����
        file_size_stream.close();
    }
    else 
    {
        file_size = 0; // ������ ������ 0���� ����
    }

    // JSON ��Ÿ������ �ۼ� (MAC, ���� ũ��, ���ø�����Ʈ, �����ð� ��)
    send_item.json_conv["MAC"] = to_string(123);   // ���� MAC ��
    send_item.json_conv["__META__"] = 
    {
        //{"SIZE", (uint32_t)file_size},         
        {"SIZE", file_size_uint32},               // ���� ũ�� ����
        {"SAMPLING_RATE", 16000},                 // ���ø�����Ʈ ����
        {"TIME", 10}                              // ���� �ð�(����)
    };
    return send_item;                             // ���� ��ȯ
}

// [��������: AI] 2-2-0 ó�� �Լ� (������� ���� ���� ��� ����)
WorkItem proc_2_2_0(WorkItem& item) 
{
    WorkItem send_item;                            // ����� WorkItem ����

    cout << "AI ���: " << item.json_conv["RESULT"] << endl;
    send_item.protocol = item.protocol;
    send_item.json_conv["RESPONSE"] = "OK";


    return send_item;                              // �� ���� ��ȯ
}

// �������ݿ� ���� ó�� �Լ� ȣ��
unordered_map<string, function<WorkItem(WorkItem& item)>> protocol_handlers =
{
    {"0-0-0", proc_0_0_0},
    {"1-0-0", proc_1_0_0},
    {"2-0-0", proc_2_0_0},
  /*  {"2-1-0", proc_2_1_0}*/
    {"2-2-0", proc_2_2_0}
};

// �������� ���ڿ��� �޾Ƽ� �˸��� ó�� �Լ� ȣ��
WorkItem protocol_recv(WorkItem& item) 
{
    WorkItem send_item;                            // ����� WorkItem ����

    // �������� �ڵ鷯 �ʿ��� �ش� ���������� ã��
    unordered_map<string, function<WorkItem(WorkItem& item) >> ::iterator it = protocol_handlers.find(item.protocol);
    if (it != protocol_handlers.end())
    {
        // �ڵ鷯�� ������, �Լ� ȣ���ؼ� ��� �޾ƿ�
        send_item = protocol_handlers[item.protocol](item);
    }
    else 
    {
        cerr << "�� �� ���� ��������: " << item.protocol << endl; // ���� ��� ��� ���
    }
    return send_item;                                 // ���� ��ȯ
}






















