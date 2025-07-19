#include "common.h"                    // 공통 구조체, 타입, 함수 정의 (WorkItem 등)
#include <fstream>                     // 파일 입출력 스트림 사용

// [프로토콜: 마이크] 0-0-0 처리 함수
WorkItem proc_0_0_0(WorkItem& item) 
{
    WorkItem send_item;                // 응답용 WorkItem 객체 생성
    string protocol = item.protocol;   // 받은 프로토콜 값 저장

    send_item.protocol = protocol;     // 응답 프로토콜도 동일하게 설정
    send_item.json_conv["RESPONSE"] = "OK"; // 응답 JSON에 "OK" 메시지

    return send_item;                  // 응답 반환
}

// [프로토콜: 관리자] 1-0-0 처리 함수 
WorkItem proc_1_0_0(WorkItem& item) // 관리자가 접속했을 때, 다른 관리자가 있는지 확인하고 응답   
{
    WorkItem send_item;                // 응답용 WorkItem 생성
    // 실제로는 관리자 중복 접속 체크 등 처리
    return send_item;                  // 현재는 빈 응답 반환
}

// [프로토콜: AI] 2-0-0 처리 함수
WorkItem proc_2_0_0(WorkItem& item) 
{
    WorkItem send_item;                // 응답용 WorkItem 생성
    string protocol = item.protocol;   // 프로토콜 값 추출

    send_item.protocol = protocol;     // 응답 프로토콜 설정
    send_item.json_conv["RESPONSE"] = "OK"; // 응답 JSON에 "OK" 설정

    return send_item;                  // 응답 반환
}

// [프로토콜: AI] 2-1-0 처리 함수 (서버가 AI에게 음성파일 전송)
WorkItem proc_2_1_0(WorkItem& item) 
{
    WorkItem send_item;                        // 응답용 WorkItem 생성
   
    send_item.protocol = item.protocol;             // 응답 프로토콜 설정

    // 파일 경로
    string audio_path = "C:\\Users\\mjy\\Downloads\\2200031642_ToyConveyor_case2_normal_IND_ch3_1642 (1).wav";

    // 파일을 이진 모드로 오픈
    ifstream file(audio_path, ios::binary);

    uint32_t file_size_uint32;                 // 파일 크기 저장용 변수
    if (file.is_open()) 
    {
        // 파일을 처음부터 끝까지 읽어서 벡터에 저장
        send_item.payload = vector<unsigned char>
        (
            istreambuf_iterator<char>(file),   // 파일 처음부터
            istreambuf_iterator<char>()        // 파일 끝까지
        );
        streamsize file_size = file.tellg(); // 현재 위치(=파일 크기) 얻기
        file.seekg(0, ios::end);                  // 파일 포인터를 처음으로 이동

        // 파일 크기를 uint32_t로 변환해서 저장
        file_size_uint32 = static_cast<uint32_t>(file_size);

        file.close();                         // 파일 닫기
    }
    else { cout << "파일 열기 실패!" << endl; }    // 파일 열기 실패 시 메시지 출력 
   
       
    // 파일 크기 구하기
    ifstream file_size_stream(audio_path, ios::binary | std::ios::ate);

    cout << "proc_2_1_0 called" << endl;

    int file_size = 0;
    if (file_size_stream.is_open()) 
    {
        file_size = static_cast<int>(file_size_stream.tellg()); // 파일 크기 int로 저장
        file_size_stream.close();
    }
    else 
    {
        file_size = 0; // 파일이 없으면 0으로 설정
    }

    // JSON 메타데이터 작성 (MAC, 파일 크기, 샘플링레이트, 녹음시간 등)
    send_item.json_conv["MAC"] = to_string(123);   // 예시 MAC 값
    send_item.json_conv["__META__"] = 
    {
        //{"SIZE", (uint32_t)file_size},         
        {"SIZE", file_size_uint32},               // 파일 크기 정보
        {"SAMPLING_RATE", 16000},                 // 샘플링레이트 정보
        {"TIME", 10}                              // 녹음 시간(예시)
    };
    return send_item;                             // 응답 반환
}

// [프로토콜: AI] 2-2-0 처리 함수 (오디오에 대한 예측 결과 수신)
WorkItem proc_2_2_0(WorkItem& item) 
{
    WorkItem send_item;                            // 응답용 WorkItem 생성

    cout << "AI 결과: " << item.json_conv["RESULT"] << endl;
    send_item.protocol = item.protocol;
    send_item.json_conv["RESPONSE"] = "OK";


    return send_item;                              // 빈 응답 반환
}

// 프로토콜에 따라 처리 함수 호출
unordered_map<string, function<WorkItem(WorkItem& item)>> protocol_handlers =
{
    {"0-0-0", proc_0_0_0},
    {"1-0-0", proc_1_0_0},
    {"2-0-0", proc_2_0_0},
  /*  {"2-1-0", proc_2_1_0}*/
    {"2-2-0", proc_2_2_0}
};

// 프로토콜 문자열을 받아서 알맞은 처리 함수 호출
WorkItem protocol_recv(WorkItem& item) 
{
    WorkItem send_item;                            // 응답용 WorkItem 생성

    // 프로토콜 핸들러 맵에서 해당 프로토콜을 찾음
    unordered_map<string, function<WorkItem(WorkItem& item) >> ::iterator it = protocol_handlers.find(item.protocol);
    if (it != protocol_handlers.end())
    {
        // 핸들러가 있으면, 함수 호출해서 결과 받아옴
        send_item = protocol_handlers[item.protocol](item);
    }
    else 
    {
        cerr << "알 수 없는 프로토콜: " << item.protocol << endl; // 없는 경우 경고 출력
    }
    return send_item;                                 // 응답 반환
}






















