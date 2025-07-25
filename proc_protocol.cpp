#include "common.h"                    // 공통 구조체, 타입, 함수 정의 (WorkItem 등)
#include <fstream>                     // 파일 입출력 스트림 사용
#include <filesystem>
#include <algorithm>

WorkItem proc_1_1_0(WorkItem& item);
WorkItem proc_2_1_0(WorkItem& item);



// DB 연결 함수
void Log_connect(int num_pin, bool state_connect)
{
   auto t = time(NULL); // 현재 시간 가져오기
   tm tm;
   localtime_s(&tm, &t); // 현재 시간을 tm 구조체로 변환  

   char time_str[20]; // 시간 문자열 저장용 버퍼
   strftime(time_str, sizeof(time_str), "%Y-%m-%d %H:%M:%S", &tm); // 시간 문자열 포맷팅

  
   string query1 = "INSERT INTO LOG_CONNECTION (STATE_CONNECT, NUM_PIN, CONNECT_TIME) VALUES (" + to_string(state_connect) + ", " + to_string(num_pin) + ", '" + time_str +"')";

   string query2 = "UPDATE LIST_PIN SET STATE_CONNECT = " + to_string(state_connect) + " WHERE NUM_PIN = " + to_string(num_pin);
      
   MYSQL* conn = connect_db();
   if (!conn)
   {
       cout << "DB 연결 실패" << endl;
   }

   // 쿼리문 실행하기
  /* if (mysql_query(conn, query1.c_str()) && mysql_query(conn, query2.c_str()))
   {
       mysql_close(conn);
       cout << "쿼리 실패" << endl;
   }*/
   if (mysql_query(conn, query1.c_str()))
   {
       cout << "쿼리 실패(INSERT): " << mysql_error(conn) << endl;
   }
   if (mysql_query(conn, query2.c_str()))
   {
       cout << "쿼리 실패(UPDATE): " << mysql_error(conn) << endl;
   }
   mysql_close(conn);

}

// [프로토콜: 마이크] 0-0-0 클라 접속 요청 시, NUM_PIN을 설정하고 응답하는 함수
WorkItem proc_0_0_0(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 객체 생성

    if (!item.json_conv.contains("MAC"))    // 맥주소가 없을때 "노" 보냄
    {
        send_item.json_conv["RESPONSE"] = "NO";
        return send_item;
    }
  
    for (int i = 0; i < list_conninfo.size(); i++)   // 현재접속되어 있는 맥이랑 접속하려는 맥 주소 비교
    {
        if (item.json_conv["MAC"] == list_conninfo[i].mac)
        {
            send_item.json_conv["RESPONSE"] = "NO";
            return send_item;
        }
    }

    string mac = item.json_conv["MAC"].get<string>();
    send_item.json_conv.clear();

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }
    
    string query = "SELECT NUM_PIN FROM LIST_PIN WHERE MAC = '" + mac + "' AND STATE = true";

    if (mysql_query(conn, query.c_str()) != 0) 
    {
        mysql_close(conn);
        send_item.json_conv["RESPONSE"] = "NO";
        return send_item;
    }

    MYSQL_RES* res = mysql_store_result(conn);
    if (!res)
    {
        mysql_close(conn);
        cout << "쿼리 결과 없음" << endl;
        send_item.json_conv["RESPONSE"] = "NO";
        return send_item;
    }

    MYSQL_ROW row = mysql_fetch_row(res);
    int num_pin = 0;

    // 쿼리 결과 확인
    if (!row || !row[0])
    {
        // 행 없음, 즉 사용하면서 MAC이 등록된 상태가 아님, 미등록 상태
        mysql_free_result(res);
        cout << "[ " << item.protocol << " ] 행 없음" << endl;
        //send_item.json_conv["RESPONSE"] = "NO";
        send_item.json_conv["RESPONSE"] = "OK";
    }
    else {
        num_pin = atoi(row[0]); // NUM_PIN 값 가져오기
        send_item.json_conv["NUM_PIN"] = num_pin; // 응답 JSON NUM_PIN 추가
        send_item.json_conv["RESPONSE"] = "OK";

        Log_connect(num_pin, true);
    }

    // ConnInfo에 저장
    for (int i = 0; i < list_conninfo.size(); i ++)
    {
        //cout << i << "번째. 소켓 : " << item.socket << " / " << list_conninfo[i].socket << endl;
        if(list_conninfo[i].socket == item.socket)
        {
            list_conninfo[i].client_id = 0; // 마이크 클라이언트 ID 설정
            list_conninfo[i].num_pin = num_pin; // NUM_PIN 설정
            list_conninfo[i].mac = mac;
            break;
		}
    }
    mysql_close(conn);


    // 클라이언트(마이크)에게 상태 송신
    send_workitem(send_item); // 응답 전송

    refresh_conninfo();

    // 클라이언트(관리자)에게 상태 송신
    send_item.protocol = "1-1-0";
    for (int i = 0; i < list_conninfo.size(); i++)
    {
        if (list_conninfo[i].client_id == 1)
        {
            send_item.socket = list_conninfo[i].socket; // 해당 마이크 소켓으로 설정
            send_item = proc_1_1_0(send_item);
            send_workitem(send_item); // 응답 전송
            break;
        }
    }

    WorkItem null_item;
    return null_item;
}


// [프로토콜: 마이크] 0-0-1 클라가 NUM_PIN을 요청했을 때, 해당 핀의 활성 상태를 반환하는 함수
WorkItem proc_0_0_1(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 객체 생성
    WorkItem null_item; 
	
    int num_pin = -1;

    if (item.json_conv.contains("NUM_PIN"))
    {
        if (item.json_conv["NUM_PIN"].is_number_integer())
        {
            num_pin = item.json_conv["NUM_PIN"].get<int>();
        }
        else if (item.json_conv["NUM_PIN"].is_string())
        {
            num_pin = stoi(item.json_conv["NUM_PIN"].get<string>());
        }
    }

    MYSQL* conn = connect_db();
    if (!conn) 
    {
        cout << "DB 연결 실패" << endl;
    }
    
    // DB에 등록된 MAC 리스트
    vector<ConnInfo> list_reg;

    // list_pin에 현재 state_connect = true면서, 등록된 핀들에 대한 MAC 목록 추출
    {
        string query = "SELECT NUM_PIN, MAC FROM LIST_PIN WHERE STATE_CONNECT = true";

        //cout << "[ " << item.protocol << " ]쿼리:" << query << endl;

        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            mysql_close(conn);
            cout << "쿼리 실패" << endl;

            return null_item;
        }

        MYSQL_RES* res = mysql_store_result(conn);
        if (!res)
        {
            mysql_close(conn);
            cout << "쿼리 결과 없음" << endl;
            return null_item;
        }

        MYSQL_ROW row = mysql_fetch_row(res);

        if (!row)
        {
            mysql_free_result(res);
            mysql_close(conn);
            cout << "[ " << item.protocol << " ] 1 행 없음" << endl;
            return null_item;
        }

        // DB상에 존재하는 클라이언트 목록화
        while (row = mysql_fetch_row(res)){
            ConnInfo tmp;
            tmp.client_id = 0;
            tmp.num_pin = row[0] ? atoi(row[0]) : 0;
            tmp.mac = row[1] ? row[1] : "";

            // DB 리스트와 현재 접속 리스트를 비교하여 값 입력
            for (ConnInfo login : list_conninfo) {
                if (login.mac == tmp.mac) {
                    tmp.socket = login.socket;
                    break;
                }
            }

            list_reg.push_back(tmp);
        }
        mysql_free_result(res);
    }

    {
        // STATE_ACTIVE 상태 조회 및 송신
        string query = "SELECT STATE_ACTIVE FROM LIST_PIN WHERE NUM_PIN = " + to_string(num_pin);

        //cout << "[ " << item.protocol << " ]쿼리:" << query << endl;

        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            mysql_close(conn);
            cout << "쿼리 실패" << endl;
            return null_item;
        }

        MYSQL_RES* res = mysql_store_result(conn);
        if (!res)
        {
            mysql_free_result(res);
            mysql_close(conn);
            cout << "쿼리 결과 없음" << endl;
            return null_item;
        }

        MYSQL_ROW row = mysql_fetch_row(res);

        if (!row)
        {
            mysql_free_result(res);
            mysql_close(conn);
            cout << "[ " << item.protocol << " ] 2 행 없음" << endl;
            return null_item;
        }
        mysql_free_result(res);

        bool state_active = false;
        if (row && row[0]) {
            int state = atoi(row[0]);      // row[0]이 "0" 또는 "1" 형태의 문자열임
            state_active = (state != 0);   // 0이면 false, 1이면 true
        }
        send_item.json_conv["STATE_ACTIVE"] = state_active;
    }

    mysql_close(conn);

    for (int i = 0; i < list_reg.size(); i++)
    {
        if (send_item.protocol == "1-1-0") {        // 직접 
            send_item.socket = list_reg[i].socket; // 해당 마이크 소켓으로 설정
            send_workitem(send_item); // 응답 전송
            break;
        }
    }

    // 클라이언트(관리자)에게 상태 송신
    send_item.protocol = "1-1-0";
    send_item = proc_1_1_0(send_item);
    send_workitem(send_item); // 응답 전송

    return null_item;
}


// [프로토콜: 마이크] 0-1-0  클라가 음성파일를 전송했을 때, 해당 데이터를 파일로 저장하고 응답하는 함수
WorkItem proc_0_1_0(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 객체 생성

	auto t = time(NULL); // 현재 시간 가져오기
    tm tm;
	localtime_s(&tm, &t); // 현재 시간을 tm 구조체로 변환  

	char time_str[20]; // 시간 문자열 저장용 버퍼

    strftime(time_str, sizeof(time_str), "%Y-%m-%d_%H-%M-%S", &tm); // 시간 문자열 포맷팅

	int num_pin = -1; // NUM_PIN 초기화
    if (item.json_conv.contains("NUM_PIN"))
    {
        if (item.json_conv["NUM_PIN"].is_number_integer())
        {
            num_pin = item.json_conv["NUM_PIN"].get<int>();
        }
        else if (item.json_conv["NUM_PIN"].is_string())
        {
            num_pin = stoi(item.json_conv["NUM_PIN"].get<string>());
        }
    }

    string ext = "wav";
    if (item.json_conv.contains("SOURCE") && item.json_conv["SOURCE"].is_string())
        ext = item.json_conv["SOURCE"].get<string>();
    
    string filename = ".\\recv_wav\\" + "recv_"s + time_str + "_" + to_string(num_pin) + "." + ext;

    //cout << "파일 저장 시도: " << filename << ", payload 크기: " << item.payload.size() << endl;

    ofstream out(filename, ios::binary);
    if (out.is_open() && !item.payload.empty())
    { 
        out.write(reinterpret_cast<const char*> (item.payload.data()), item.payload.size());
        out.close();
        /*cout << "오디오 저장 성공! (" << filename << ")" << endl;*/
    }
    else { cout << "파일 저장 실패!" << endl; }    // 파일 저장 실패 시 메시지 출력 

    send_item.socket = NULL;

    // 어떤 소켓에게 보낼지 결정
    for (ConnInfo & conn : list_conninfo) 
    {
        if (conn.client_id == 2) 
        {
            send_item.socket = conn.socket; 
            break;
        }
	}

    send_item.protocol = "2-1-0";
    send_item.json_conv = item.json_conv;
    send_item.json_conv["FILE_PATH"] = filename;
    send_item.payload = item.payload;

    // AI에게 판독 전송
    WorkItem send_item_ai = proc_2_1_0(send_item);

    return send_item_ai;                  // 응답 반환
}

// [프로토콜: 관리자] 1-0-0 처리 함수 
WorkItem proc_1_0_0(WorkItem& item) // 관리자가 접속했을 때, 다른 관리자가 있는지 확인하고 응답   
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성
    // 실제로는 관리자 중복 접속 체크 등 처리
    for (int i = 0; i < list_conninfo.size(); i++) 
    {
        if (list_conninfo[i].socket == item.socket) 
        {
            list_conninfo[i].client_id = 1;

            break;
        }
    }
    refresh_conninfo();

	int cnt = 0; // 접속한 관리자 수 초기화
    for (int i = 0; i < list_conninfo.size(); i++) 
    {
        if (list_conninfo[i].client_id == 1)    // 관리자 ID가 1인 경우
        {
            cnt++; // 관리자 수 증가
		}   
    }
    if (cnt >1)    // 관리자 수가 1명 초과인 경우
    {
        send_item.json_conv["RESPONSE"] = "NO"; // 응답 JSON에 "NO" 메시지
    } 
    else 
    {
        send_item.json_conv["RESPONSE"] = "OK"; // 응답 JSON에 "OK" 메시지
	}

    return send_item;                  
}


// [프로토콜: 관리자] 1-0-1 관리자가 서버에게 고장 원인 목록 요청
WorkItem proc_1_0_1(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }

	string query = "SELECT CODE_ERROR, NAME_ERROR FROM LIST_CODE_ERROR WHERE STATE = TRUE";

    //cout << "쿼리문: " << query << endl;

    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "쿼리 실패" << endl;

        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);
    if (!res) 
    {
        mysql_close(conn);
        cout << "쿼리 결과 없음" << endl;
        return send_item;
    }

    MYSQL_ROW row;
    json arr = json::array();

    while ((row = mysql_fetch_row(res))) 
    {
        json obj_data;
        obj_data["CODE"] = row[0] ? atoi(row[0]) : 0;
        obj_data["NAME"] = row[1] ? row[1] : "";
        arr.push_back(obj_data);
    }

    send_item.json_conv["DATA"] = arr;

    mysql_free_result(res);
    mysql_close(conn);

    return send_item;
}



// [프로토콜 : 관리자]  1-0-2 서버에게 CODE_ANOMALY 목록 요청
WorkItem proc_1_0_2(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }

    string query = "SELECT CODE_ANOMALY, NAME_ANOMALY FROM LIST_CODE_ANOMALY WHERE STATE = TRUE";
   
    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "쿼리 실패" << endl;

        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);
    if (!res)
    {
        mysql_close(conn);
        cout << "1-0-2 쿼리 결과 없음" << endl;
        return send_item;
    }

    MYSQL_ROW row;
    json arr = json::array();

    while ((row = mysql_fetch_row(res))) 
    {
        json obj_data;
        obj_data["CODE"] = row[0] ? atoi(row[0]) : 0;
        obj_data["NAME"] = row[1] ? row[1] : "";
        arr.push_back(obj_data);
    }

    send_item.json_conv["DATA"] = arr;

    mysql_free_result(res);
    mysql_close(conn);

    return send_item;
}



// [프로토콜: 관리자] 1-0-3 핀 목록 테이블에 등록되지 않은 MAC 번호 요청
WorkItem proc_1_0_3(WorkItem& item)
{
    WorkItem send_item = item;

    cout << "-----------------------------------" << endl;
    refresh_conninfo();
    cout << "-----------------------------------" << endl;

    send_item.json_conv.clear();
    json mac_list = json::array(); // mac 정보 배열 생성

    for (int i = 0; i < list_conninfo.size(); i++)
    {
        if(list_conninfo[i].num_pin == 0 && list_conninfo[i].client_id == 0) mac_list.push_back(list_conninfo[i].mac); // 배열에 추가
    }

    for(int i = 0; i < mac_list.size(); i++)
    {
        cout << "mac_list[" << i << "] : " << mac_list[i] << endl;
	}
    
    send_item.json_conv["DATA"] = mac_list; // JSON에 마이크 정보 배열 추가

    return send_item;
}


// [프로토콜: 관리자] 1-1-0   관리자가 서버에게 STATE 1 마이크 목록 요청
WorkItem proc_1_1_0(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성

    send_item.json_conv.clear();
  
    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "1-1-0 DB 연결 실패" << endl;
    }

    string query = "SELECT NUM_PIN, NAME_PIN, NUM_MAP, NAME_LOC, NAME_MANAGER, MAC, DATE_REG, STATE_ACTIVE, POS_X, POS_Y, STATE_CONNECT FROM LIST_PIN WHERE STATE = TRUE";

    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str())) 
    {
        mysql_close(conn);
        cout << "1-1-0 쿼리 실패" << endl;
		send_item.json_conv["PROTOCOL"] = item.protocol; // 프로토콜 정보 추가
		send_item.json_conv["DATA"] = json::array(); // 빈 배열로 초기화
		return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);

    if (!res) 
    {
        mysql_close(conn);
        cout << "1-1-0 쿼리 결과 없음" << endl;
		send_item.json_conv["PROTOCOL"] = item.protocol; // 프로토콜 정보 추가
		send_item.json_conv["DATA"] = json::array(); // 빈 배열로 초기화
        return send_item; // 빈 응답 반환
    }

    MYSQL_ROW row;
    json pin_list = json::array(); // 마이크 정보 배열 생성

    //int cnt = 0;
    while((row = mysql_fetch_row(res))) 
    {
        json pin_info;

        pin_info["NUM_PIN"] = row[0] ? atoi(row[0]) : 0;
        pin_info["NAME_PIN"] = row[1] ? row[1] : "";
        pin_info["NUM_MAP"] = row[2] ? atoi(row[2]) : 0;
        pin_info["NAME_LOC"] = row[3] ? row[3] : "";
        pin_info["NAME_MANAGER"] = row[4] ? row[4] : "";
        pin_info["MAC"] = row[5] ? row[5] : "";
        pin_info["DATE_REG"] = row[6] ? row[6] : "";

        pin_info["STATE_ACTIVE"] = row[7] ? (strcmp(row[7], "1") == 0) : false;
     
        pin_info["POS_X"] = row[8] ? atof(row[8]) : 0.0;
        pin_info["POS_Y"] = row[9] ? atof(row[9]) : 0.0;
        pin_info["STATE_CONNECT"] = row[10] ? (strcmp(row[10], "1") == 0 ? true : false) : false;
        pin_list.push_back(pin_info); // 배열에 추가
	}
	send_item.json_conv["DATA"] = pin_list; // JSON에 마이크 정보 배열 추가

    mysql_free_result(res);  // 쿼리 결과 메모리 해제
	mysql_close(conn);       // DB 연결 종료

    return send_item;               
}


// [프로토콜: 관리자] 1-1-1 // 특정 마이크 가동/ 중지
WorkItem proc_1_1_1(WorkItem& item) 
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성

	send_item.json_conv.clear(); // JSON 초기화

	int num_pin = item.json_conv.value("NUM_PIN", -1); // NUM_PIN 값 가져오기
	int state_active = item.json_conv.value("STATE_ACTIVE", -1); // STATE_ACTIVE 값 가져오기

    if(num_pin == -1 || state_active == -1) 
    {
        send_item.json_conv["RESPONSE"] = "NO"; // 잘못된 요청에 대한 응답
        return send_item; // 빈 응답 반환
	}

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }
    
    string query = "UPDATE LIST_PIN SET STATE_ACTIVE = " + to_string(state_active) + " WHERE NUM_PIN = " + to_string(num_pin);

    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "쿼리 실패" << endl;
        
        return send_item; // 빈 응답 반환
    }

    mysql_close(conn);


    // 어떤 소켓에게 보낼지 결정
    send_item.socket = NULL;

    for (ConnInfo& conn : list_conninfo)
    {
        if (conn.client_id == 0 && conn.num_pin == num_pin)
        {
            send_item.socket = conn.socket;
            break;
        }
    }

    send_item.protocol = "0-0-1";
    send_item.json_conv = item.json_conv;

    return send_item;                  // 현재는 빈 응답 반환
}


// [프로토콜: 관리자] 1-1-2  // 전체 마이크 가동 /중지
WorkItem proc_1_1_2(WorkItem& item)
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성


    int state_active = item.json_conv.value("STATE_ACTIVE", -1); // STATE_ACTIVE 값 가져오기

    send_item.json_conv.clear();              // JSON 초기화

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }

    // 일일 이상상황에서 이상상황이 발생한 경우(code_anomaly가 1인 경우)는 송신하지 않게 목록화
    string query = "SELECT NUM_PIN FROM LIST_EVENT_DAILY WHERE CODE_ANOMALY != 1";
    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "쿼리 실패" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);
    if (!res)
    {
        mysql_close(conn);
        cout << "1-1-2 쿼리 결과 없음" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_ROW row;
    vector<int> pin_list;       // code_anomaly != 1 에 대한 핀 번호만 넣기, 아래에서 현재 이상상황인 핀에 대한 차집합용

    while ((row = mysql_fetch_row(res)))
    {
        int pin_num = row[0] ? atoi(row[0]) : 0;      // INT
 
        pin_list.push_back(pin_num);
    }

    mysql_free_result(res);  // 쿼리 결과 메모리 해제

    /////////////////////////////////////////////////
    string in_list;
    for (int i = 0; i < list_conninfo.size(); i++) 
    {
        // 마이크이면서, list_pin에 존재하는 핀만 실행
        if (list_conninfo[i].client_id == 0 && list_conninfo[i].num_pin > 0)
        {
            for (int j = 0; j < pin_list.size(); j++) {
                // 위에서 현재 이상상황이 발생한 목록과 현재 접속된 목록을 대조하여 이상상황이 아닌 경우만 업데이트 목록에 추가
                if (pin_list[j] == list_conninfo[i].num_pin) {
                    if (!in_list.empty()) in_list += ",";
                    in_list += to_string(list_conninfo[i].num_pin);
                    break;
                }
            }            
        }
    }
    if (!in_list.empty())
    {
        string query = "UPDATE LIST_PIN SET STATE_ACTIVE = " + to_string(state_active)
            + " WHERE NUM_PIN IN (" + in_list + ")";

        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            mysql_close(conn);
            cout << "쿼리 실패" << endl;
            return send_item; // 빈 응답 반환
        }

        mysql_close(conn);       // DB 연결 종료
    }

    // json 수정
    send_item.protocol = "0-0-1";
    send_item.json_conv.clear();

    if(state_active) send_item.json_conv["STATE_ACTIVE"] = true;
    else send_item.json_conv["STATE_ACTIVE"] = false;
	

    for(int i = 0; i < list_conninfo.size(); i++) 
    {
        for (int j = 0; j < pin_list.size(); j++) {
            if (pin_list[j] == list_conninfo[i].num_pin && list_conninfo[i].num_pin > 0) {
                send_item.socket = list_conninfo[i].socket; // 해당 마이크 소켓으로 설정
                send_workitem(send_item); // 응답 전송
                break;
            }
        }
	}

    WorkItem null_item;
    return null_item;
}



// [프로토콜: 관리자] 1-2-0  이상 상황 발생한 핀 목록 요청
WorkItem proc_1_2_0(WorkItem& item)
{
    WorkItem send_item = item;

    bool flag_arr = false;
    if (!item.protocol.empty()) flag_arr = true;

    send_item.json_conv.clear(); // JSON 초기화

    MYSQL* conn = connect_db();
    if (!conn) 
    {
        cout << "DB 연결 실패" << endl;
    }
    string query =
        "SELECT "
        "PIN.NUM_PIN, "            // 0: 핀 번호

        "EVT.NUM_EVENT, "   // 5: 이벤트 번호(일련번호)
        "EVT.CODE_ERROR, "         // 6: 원인 코드
        "EVT.CODE_ANOMALY, "       // 7: 상태 코드
        "EVT.MANAGER_PROC, "       // 8: 처리자 이름
        "EVT.MEMO, "               // 9: 메모
        "EVT.DATE_START, "         // 10: 발생일자
        "EVT.DATE_END "            // 11: 종료일자
        "FROM LIST_EVENT_DAILY EVT "
        "INNER JOIN LIST_PIN PIN ON EVT.NUM_PIN = PIN.NUM_PIN "
        "INNER JOIN LIST_MAP MAP ON PIN.NUM_MAP = MAP.NUM_MAP "
        "ORDER BY NUM_EVENT";

	if (!flag_arr) query += " DESC LIMIT 1"; // 2-2-0 프로토콜은 활성화된 이벤트만 조회

    send_item.protocol = "1-2-0";
    
    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "1-2-0 쿼리 실패" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);

    if (!res)
    {
        mysql_close(conn);
        cout << "1-2-0 쿼리 결과 없음" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_ROW row;
    json pin_list = json::array(); // 마이크 정보 배열 생성

    while ((row = mysql_fetch_row(res)))
    {
        json pin_info;
        pin_info["NUM_PIN"] = row[0] ? atoi(row[0]) : 0;      // INT
        
        pin_info["NUM_EVENT"] = row[1] ? atoi(row[1]) : 0;      // INT
        pin_info["CODE_ERROR"] = row[2] ? atoi(row[2]) : 0;      // INT
        pin_info["CODE_ANOMALY"] = row[3] ? atoi(row[3]) : 0;      // INT
        pin_info["MANAGER_PROC"] = row[4] ? row[4] : "";           // VARCHAR
        pin_info["MEMO"] = row[5] ? row[5] : "";           // VARCHAR
        pin_info["DATE_START"] = row[6] ? row[6] : "";         // DATETIME(문자열)
        pin_info["DATE_END"] = row[7] ? row[7] : "";         // DATETIME(문자열)
        pin_list.push_back(pin_info);
    }
    send_item.json_conv["DATA"] = pin_list;

    mysql_free_result(res);  // 쿼리 결과 메모리 해제
    mysql_close(conn);       // DB 연결 종료

    return send_item;
}


// [프로토콜: 관리자] 1-2-1  관리자가 보낸 핀 처리 항목 디비에 저장하기
WorkItem proc_1_2_1(WorkItem& item)
{
    WorkItem send_item = item;
	send_item.json_conv.clear(); // JSON 초기화

    int num_event = item.json_conv.value("NUM_EVENT", -1);
    int code_anomaly = item.json_conv.value("CODE_ANOMALY", -1);
    string manager_proc = item.json_conv.value("MANAGER_PROC", "");
    string memo = item.json_conv.value("MEMO", "");
    //string date_end = item.json_conv.value("DATE_END", "");

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    } 

    string query = "UPDATE LIST_EVENT_DAILY SET "
        "CODE_ANOMALY  = " + to_string(code_anomaly) + 
        ", MANAGER_PROC = '" + manager_proc + "'" +
        ", MEMO = '" + memo + "'" + 
        ", DATE_END = NOW()" +
        " WHERE NUM_EVENT = " + to_string(num_event);


    //cout << "쿼리: " << query << endl;M
    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "쿼리 실패" << endl;

        return send_item; // 빈 응답 반환
    }

    mysql_close(conn);
    
    // 1-2-0을 송신
    send_item.protocol = "1-2-0";
    send_item = proc_1_2_0(send_item);
    return send_item;
}



// 요청일 전체 기록 요청
WorkItem proc_1_2_2(WorkItem& item)
{
    WorkItem send_item = item;

    send_item.json_conv.clear(); // JSON 초기화

    string date_req = item.json_conv.value("DATE_REQ", "");

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }


    string query =
        "SELECT "
        "PIN.NUM_PIN, "            // 0: 핀 번호
        "PIN.NAME_PIN, "           // 1: 핀 이름
        "MAP.NAME_MAP, "           // 2: 지도 이름
        "PIN.NAME_LOC, "           // 3: 위치 이름
        "PIN.NAME_MANAGER, "       // 4: 담당자 이름

        "EVT.NUM_DAILY, "   // 5: 이벤트 번호(일련번호)
        "EVT.CODE_ERROR, "         // 6: 원인 코드
        "EVT.CODE_ANOMALY, "       // 7: 상태 코드
        "EVT.MANAGER_PROC, "       // 8: 처리자 이름
        "EVT.MEMO, "               // 9: 메모
        "EVT.DATE_START, "         // 10: 발생일자
        "EVT.DATE_END "            // 11: 종료일자
        "FROM LIST_EVENT_TOTAL EVT "
        "INNER JOIN LIST_PIN PIN ON EVT.NUM_PIN = PIN.NUM_PIN "
        "INNER JOIN LIST_MAP MAP ON PIN.NUM_MAP = MAP.NUM_MAP "
        "WHERE DATE(EVT.DATE_START) = '" + date_req + "'"
        "ORDER BY NUM_DAILY DESC";

    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "1-2-2쿼리 실패" << endl;

        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);

    if (!res)
    {
        mysql_close(conn);
        cout << "1-2-2 쿼리 결과 없음" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_ROW row;
    json date_list = json::array(); // 지도 정보 배열 생성


    while ((row = mysql_fetch_row(res))) {
        json date_info;
        date_info["NUM_PIN"] = row[0] ? atoi(row[0]) : 0;      // INT
        date_info["NAME_PIN"] = row[1] ? row[1] : "";           // VARCHAR
        date_info["NAME_MAP"] = row[2] ? row[2] : "";           // VARCHAR
        date_info["NAME_LOC"] = row[3] ? row[3] : "";           // VARCHAR
        date_info["NAME_MANAGER"] = row[4] ? row[4] : "";           // VARCHAR

        date_info["NUM_EVENT"] = row[5] ? atoi(row[5]) : 0;      // INT
        date_info["CODE_ERROR"] = row[6] ? atoi(row[6]) : 0;      // INT
        date_info["CODE_ANOMALY"] = row[7] ? atoi(row[7]) : 0;      // INT
        date_info["MANAGER_PROC"] = row[8] ? row[8] : "";           // VARCHAR
        date_info["MEMO"] = row[9] ? row[9] : "";           // VARCHAR
        date_info["DATE_START"] = row[10] ? row[10] : "";         // DATETIME(문자열)
        date_info["DATE_END"] = row[11] ? row[11] : "";         // DATETIME(문자열)
        date_list.push_back(date_info);
    }

    send_item.json_conv["DATA"] = date_list;

    mysql_free_result(res);  // 쿼리 결과 메모리 해제
    mysql_close(conn);       // DB 연결 종료

    return send_item;
}



// 관리자가 요청일에 맞게 내용 수정/삭제 업데이트
WorkItem proc_1_2_3(WorkItem& item)
{
    WorkItem send_item = item;
    // 빈 응답 반환
    WorkItem null_item;

    send_item.json_conv.clear(); // JSON 초기화

    string date_req = string(item.json_conv.value("DATE_REQ", "")).substr(0, 10);

    json modified_list = json::array();
    modified_list = item.json_conv["MODIFIED"];

	json removed_list = json::array();
	removed_list = item.json_conv["REMOVED"];

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
        return null_item;
    }
    
    if (item.json_conv.contains("MODIFIED") && item.json_conv["MODIFIED"].is_array())
    {
		//cout << "MODIFIED 배열 크기: " << modified_list.size() << endl;

        for (auto& date : modified_list)
        {
            //cout << "모디팔이: " << modified_list.size() << endl;

            int num_event = date.value("NUM_EVENT", 0);
            int code_anomaly = date.value("CODE_ANOMALY", 0);
            string manager_proc = date.value("MANAGER_PROC", "");
            string memo = date.value("MEMO", "");


           string query = "UPDATE LIST_EVENT_TOTAL SET " 
                "CODE_ANOMALY = " + to_string(code_anomaly) +
                ", MANAGER_PROC = '" + manager_proc + "'" +
                ", MEMO = '" + memo + "'" +
                " WHERE NUM_DAILY = " + to_string(num_event) +
                " AND DATE(DATE_START) = '" + date_req + "'";
            
           /* string query = "UPDATE LIST_EVENT_TOTAL SET CODE_ANOMALY = " + to_string(code_anomaly) +
                ", MANAGER_PROC = " + manager_proc +
                ", MEMO = " + memo +
                ", DATE_END = " + date_end +
                " WHERE NUM_DAILY = " + to_string(num_event) +
                " AND DATE_START = " + date_req;*/

            // 쿼리문 실행하기
            if (mysql_query(conn, query.c_str()))
            {
                mysql_close(conn);
                cout << "쿼리 실패" << endl;
            }
        }
    }

    if (item.json_conv.contains("REMOVED") && item.json_conv["REMOVED"].is_array())
    {
		//cout << "REMOVED 배열 크기: " << removed_list.size() << endl;

        for (int i = 0 ; i < removed_list.size(); i++)
        {
            int num = removed_list[i];

            /*string query = "DELETE FROM LIST_EVENT_TOTAL WHERE NUM_DAILY = " +
                to_string(num) +
                " AND DATE_START = '" + date_req + "'";*/

            string query = "DELETE FROM LIST_EVENT_TOTAL WHERE NUM_DAILY = " + to_string(num) + " AND DATE(DATE_START) = '" + date_req + "'";



            cout << "쿼리문: " << query << endl;
            /* string query = "DELETE FROM LIST_EVENT_TOTAL WHERE NUM_DAILY = " +
                 to_string(num) +
                 " AND DATE_START = '" + date_req + "'";*/


                 // 쿼리문 실행하기
            if (mysql_query(conn, query.c_str()))
            {
                mysql_close(conn);
                cout << "쿼리 실패" << endl;
            }
        }
    }

    return null_item;
}



// [프로토콜: 관리자] 1-3-0  지도 목록 및 정보 요청
WorkItem proc_1_3_0(WorkItem& item)
{
    //cout << "1-3-0 들옴" << endl;
    WorkItem send_item = item;

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }

    string query = "SELECT NUM_MAP, NAME_MAP, INDEX_MAP, PATH_MAP, PATH_LOCAL, SIZE_MAP FROM LIST_MAP WHERE STATE = TRUE";
 
    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "쿼리 실패" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);

    if (!res)
    {
        mysql_close(conn);
        cout << "쿼리 결과 없음" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_ROW row;
    json map_list = json::array(); // 지도 정보 배열 생성



    while ((row = mysql_fetch_row(res)))
    {
        json map_info;
        map_info["NUM_MAP"] = row[0] ? atoi(row[0]) : 0;
        map_info["NAME_MAP"] = row[1] ? row[1] : "";
        map_info["INDEX_MAP"] = row[2] ? atoi(row[2]) : 0;
        string path_map = row[3] ? row[3] : "";
        
        map_info["PATH_LOCAL"] = row[4] ? row[4] : "";
        map_info["SIZE_MAP"] = row[5] ? atoi(row[5]) : 0;
        map_list.push_back(map_info); // 배열에 추가
    }
    send_item.json_conv["DATA"] = map_list;

    mysql_free_result(res);  // 쿼리 결과 메모리 해제
    mysql_close(conn);       // DB 연결 종료

    return send_item;
}



// [프로토콜: 관리자] 1-3-1  지도 목록 수정 및 삭제
WorkItem proc_1_3_1(WorkItem& item)
{
    WorkItem send_item = item;

    send_item.json_conv.clear(); // JSON 초기화

    json modified_list = json::array();
    modified_list = item.json_conv["MODIFIED"];
   
    json removed_list = json::array();
    removed_list = send_item.json_conv["REMOVED"];

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "1-3-1 DB 연결 실패" << endl;
    }

    if (item.json_conv.contains("MODIFIED") && item.json_conv["MODIFIED"].is_array())
    {
        auto arr = item.json_conv["MODIFIED"];

        for (auto& date : arr)
        {
            int num_map = date.value("NUM_MAP", 0);
            string name_map = date.value("NAME_MAP", "");
            int index_map = date.value("INDEX_MAP", 0);
            string path_local = date.value("PATH_LOCAL", "");
            int size_map = date.value("SIZE_MAP", 0);
            
            string safe_path_local = path_local;
            size_t pos2 = 0;
            while ((pos2 = safe_path_local.find("\\", pos2)) != string::npos)
            {
                safe_path_local.replace(pos2, 1, "\\\\");
                pos2 += 2; // '\\' 두 글자만큼 이동
            }

            string query = "UPDATE LIST_MAP SET "
                "NAME_MAP = '" + name_map + "', "
                "INDEX_MAP = " + to_string(index_map) + ", "
                "PATH_LOCAL = '" + safe_path_local + "', "
                "SIZE_MAP = " + to_string(size_map) + " "
                "WHERE NUM_MAP = " + to_string(num_map);

            // 쿼리문 실행하기
            if (mysql_query(conn, query.c_str()))
            {
                mysql_close(conn);
                //mysql_close(conn);
                cout << "1-3-1 쿼리 실패" << endl;
                return send_item;
            }
        }
        auto arr2 = item.json_conv["REMOVED"];
        for (auto& remove : arr2)
        {
			string query = "UPDATE LIST_MAP SET STATE = FALSE WHERE NUM_MAP = " + to_string(remove);
                
            // 쿼리문 실행하기
            if (mysql_query(conn, query.c_str()))
            {
                //mysql_close(conn);
                cout << "1-3-1 쿼리 실패" << endl;
                return send_item;
            }
        }
    }
    WorkItem null_item;
    //return send_item;
    return null_item;
}


// 지도 파일 수신
WorkItem proc_1_3_2(WorkItem& item)
{
	cout << "1-3-2 지도 파일 수신" << endl;   
    WorkItem send_item = item;

	//cout << "파일이름: " << item.json_conv["__META__"]["NAME"] << endl;

	string name = item.json_conv["__META__"]["NAME"];

    string filename = "C:\\Users\\mjy\\source\\repos\\ConsoleApplication1\\maps\\" + name;

	//cout << "지도 파일 이름: " << filename << endl;

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "1-3-2 DB 연결 실패" << endl;
    }
	//cout << "지도 파일 저장 시도: " << filename << ", payload 크기: " << item.payload.size() << endl;
    
    ofstream out(filename, ios::binary);
    if (out.is_open() && !item.payload.empty())
    {
        out.write(reinterpret_cast<const char*> (item.payload.data()), item.payload.size());
        out.close();
        cout << "1-3-2 지도 저장 성공! (" << filename << ")" << endl;
        cout << endl;
    }
    else { cout << "1-3-2 지도 저장 실패!" << endl; }    // 지도 저장 실패 시 메시지 출력 


    int index_map = send_item.json_conv["INDEX_MAP"];
    string name_map = send_item.json_conv["NAME_MAP"];
    string path_local = send_item.json_conv["__META__"]["PATH"];
    int size_map = send_item.json_conv["__META__"]["SIZE"];
    
    // 1. filename의 '\'를 '\\'로 변환
    string safe_filename = filename;
    size_t pos = 0;
    while ((pos = safe_filename.find("\\", pos)) != string::npos) 
    {
        safe_filename.replace(pos, 1, "\\\\");
        pos += 2; // '\\' 두 글자만큼 이동
    }

    string safe_path_local = path_local;
    size_t pos2 = 0;
    while ((pos2 = safe_path_local.find("\\", pos2)) != string::npos)
    {
        safe_path_local.replace(pos2, 1, "\\\\");
        pos2 += 2; // '\\' 두 글자만큼 이동
    }

    string query = "INSERT INTO LIST_MAP (NAME_MAP, INDEX_MAP, PATH_MAP, STATE, PATH_LOCAL, SIZE_MAP) VALUES('"+ name_map + "', " + to_string(index_map) + ", '" + safe_filename + "', 1, '" + safe_path_local + "', "
        + to_string(size_map) + ")";
    //cout << "1-3-2 쿼리: " << query << endl;
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "1-3-2 쿼리 실패" << endl;
    }

    WorkItem null_item;
    return null_item;
}



// [프로토콜: 관리자] 1-3-3  관리자가 지도 파일이 없는 경우 지도 정보 요청
WorkItem proc_1_3_3(WorkItem& item)
{
    WorkItem send_item = item;

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "DB 연결 실패" << endl;
    }

    string query = "SELECT NUM_MAP, INDEX_MAP, NAME_MAP, PATH_MAP , PATH_LOCAL, SIZE_MAP FROM LIST_MAP WHERE STATE = TRUE AND NUM_MAP = " + to_string(item.json_conv["NUM_MAP"]);

    // 쿼리문 실행하기
    if (mysql_query(conn, query.c_str()))
    {
        mysql_close(conn);
        cout << "1-3-3 쿼리 실패" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_RES* res = mysql_store_result(conn);

    if (!res)
    {
        mysql_close(conn);
        cout << "1-3-3 쿼리 결과 없음" << endl;
        return send_item; // 빈 응답 반환
    }

    MYSQL_ROW row;
  
    while ((row = mysql_fetch_row(res)))
    {
        json map_info;
        map_info["NUM_MAP"] = row[0] ? atoi(row[0]) : 0;
        map_info["INDEX_MAP"] = row[1] ? atoi(row[1]) : 0;
        map_info["NAME_MAP"] = row[2] ? row[2] : "";

        string file_path = row[3] ? row[3] : "";
        string path_local = row[4] ? row[4] : "";
        int size_map = row[5] ? atoi(row[5]) : 0;


        // 마지막 역슬래시(\\) 위치 찾기
        size_t pos = file_path.find_last_of('\\');

        // pos가 발견됐으면 그 다음부터 끝까지, 못 찾으면 전체 반환
        string file_name = (pos != string::npos) ? file_path.substr(pos + 1) : file_path;

        cout << file_name << endl; // 결과: map_cad1.jpg


		uint32_t file_size = 0;

        // 파일을 이진 모드로 오픈
        ifstream file(file_path, ios::binary);

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
        else { cout << "1-3-3 파일 열기 실패!" << endl; }    // 파일 열기 실패 시 메시지 출력 


        // 파일 크기 구하기
        ifstream file_size_stream(file_path, ios::binary | std::ios::ate);

        map_info["__META__"] =
        {
            {"NAME", file_name},
            {"SIZE", size_map},
            {"PATH", path_local}
        };

		send_item.json_conv = map_info; // JSON에 맵 정보 추가
    }

    mysql_free_result(res);  // 쿼리 결과 메모리 해제
    mysql_close(conn);       // DB 연결 종료

    return send_item;
}


// [프로토콜: 관리자] 1-3-4 핀 목록 수정 및 추가
WorkItem proc_1_3_4(WorkItem& item)
{
	//cout << "1-3-4 핀 목록 수정 및 추가" << endl;
    WorkItem send_item = item;

    json add_list = json::array(); // 핀 추가 배열 생성
    add_list = send_item.json_conv["ADD"];
   
    json modified_list = json::array();
    modified_list = send_item.json_conv["MODIFIED"];
    
    
    json remove_list = json::array();
    remove_list = send_item.json_conv["REMOVED"];

    MYSQL* conn = connect_db();
    if (!conn)
    {
        cout << "1-3-4 DB 연결 실패" << endl;
    }

    for (int i = 0; i < add_list.size(); i++)
    {
        int num_pin = add_list[i]["NUM_PIN"]; // NUM_PIN 값 가져오기
        int num_map = add_list[i]["NUM_MAP"];
        string name_pin = add_list[i]["NAME_PIN"];
        string name_loc = add_list[i]["NAME_LOC"];
        string name_manager = add_list[i]["NAME_MANAGER"];
        string mac = add_list[i]["MAC"];
		double pos_x = add_list[i]["POS_X"];
		double pos_y = add_list[i]["POS_Y"];


       /* string query = "INSERT INTO LIST_PIN (NUM_PIN, NUM_MAP, NAME_PIN, NAME_LOC, NAME_MANAGER, MAC, POS_X, POS_Y) VALUES (" 
                        + to_string(num_pin) + ", " + to_string(num_map) + ", '" + name_pin + "', '" + name_loc + "', '" + name_manager + "', '" + mac + "', " + to_string(pos_x) + ", " + to_string(pos_y) + ")";
       */

        string query = "INSERT INTO LIST_PIN "
            "(NUM_PIN, NUM_MAP, NAME_PIN, NAME_LOC, NAME_MANAGER, MAC, POS_X, POS_Y, STATE_CONNECT) VALUES ("
            + to_string(num_pin) + ", "
            + to_string(num_map) + ", '"
            + name_pin + "', '"
            + name_loc + "', '"
            + name_manager + "', '"
            + mac + "', "
            + to_string(pos_x) + ", "
            + to_string(pos_y) + ", 1)";



        //cout << "쿼리: " << query << endl;
		// 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            mysql_close(conn);
            cout << "1-3-4 쿼리 실패" << endl;
            return send_item; 
		}

        // 접속목록 업데이트 
        for (int i = 0; i < list_conninfo.size(); i++) {
            if (list_conninfo[i].mac == mac) list_conninfo[i].num_pin = num_pin;
        }
    }

    for (int i =0; i < modified_list.size(); i++)
    {
        int num_pin = modified_list[i]["NUM_PIN"]; // NUM_PIN 값 가져오기
        string name_pin = modified_list[i]["NAME_PIN"];
        string name_loc = modified_list[i]["NAME_LOC"];
		string name_manager = modified_list[i]["NAME_MANAGER"];

        string query = "UPDATE LIST_PIN SET NUM_PIN =  " + to_string(num_pin) +
            ", NAME_PIN = '" + name_pin +
            "', NAME_LOC = '" + name_loc +
            "', NAME_MANAGER = '" + name_manager +
            "' WHERE NUM_PIN = " + to_string(modified_list[i]["NUM_PIN"]);
        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            mysql_close(conn);
            cout << "1-3-4 쿼리 실패" << endl;
        }
	}
   
    for(int i = 0; i < remove_list.size(); i++)
    {
        int num_pin = remove_list[i]; // NUM_PIN 값 가져오기
        string query = "UPDATE LIST_PIN SET STATE = FALSE WHERE NUM_PIN = " + to_string(num_pin);
        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            mysql_close(conn);
            cout << "1-3-4 쿼리 실패" << endl;
        }

        // 접속 목록 업데이트
        for (int i = 0; i < list_conninfo.size(); i++) {
            if (list_conninfo[i].num_pin == num_pin) {
                list_conninfo[i].num_pin = 0;
                break;
            }
        }
	}



    WorkItem null_item;
    return null_item;
}




// [프로토콜: AI] 2-0-0 처리 함수
WorkItem proc_2_0_0(WorkItem& item) 
{
    WorkItem send_item = item;                // 응답용 WorkItem 생성

    cout << item.json_conv.dump().c_str() << endl; // JSON 내용 출력 (디버깅용)

    for (int i = 0; i < list_conninfo.size(); i++) {
        if (list_conninfo[i].socket == item.socket) {
            list_conninfo[i].client_id = 2;
            break;
        }
    }
    refresh_conninfo();

    send_item.json_conv["RESPONSE"] = "OK"; // 응답 JSON에 "OK" 설정

    return send_item;                  // 응답 반환
}


//==== 파일 경로 수정!!=======
// [프로토콜: AI] 2-1-0 처리 함수 (서버가 AI에게 음성파일 전송)
WorkItem proc_2_1_0(WorkItem& item) 
{
    WorkItem send_item = item;                        // 응답용 WorkItem 생성

    // 파일 경로
    //string audio_path = "C:\\Users\\mjy\\Downloads\\2200031642_ToyConveyor_case2_normal_IND_ch3_1642 (1).wav";

    string audio_path = send_item.json_conv["FILE_PATH"];

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

    return send_item;                             // 응답 반환
}

// [프로토콜: AI] 2-2-0 처리 함수 (오디오에 대한 예측 결과 수신)
WorkItem proc_2_2_0(WorkItem& item) 
{
    WorkItem send_item = item;                            // 응답용 WorkItem 생성

	int num_pin = -1, cls = -1; // NUM_PIN과 CLASS 초기화

    if (item.json_conv.contains("NUM_PIN")) 
    {
        if (item.json_conv["NUM_PIN"].is_number_integer()) num_pin = item.json_conv["NUM_PIN"].get<int>();
        else if (item.json_conv["NUM_PIN"].is_string()) num_pin = std::stoi(item.json_conv["NUM_PIN"].get<string>());
    }
    if (item.json_conv.contains("CLASS")) 
    {
        if (item.json_conv["CLASS"].is_number_integer()) cls = item.json_conv["CLASS"].get<int>();
        else if (item.json_conv["CLASS"].is_string()) cls = std::stoi(item.json_conv["CLASS"].get<string>());
    }
    string result = item.json_conv.value("RESULT", "");
    //string filename = item.json_conv.value("FILE_PATH", "");
    if (result == "ABNORMAL")
    {
        MYSQL* conn = connect_db();
        if (!conn) 
        {
            cout << "DB 연결 실패" << endl;
        }
        string filename = item.json_conv.value("FILE_PATH", "");
    
        // 일일 기록 추가
        string query = "INSERT INTO LIST_EVENT_DAILY (NUM_PIN, CODE_ERROR, CODE_ANOMALY, FILE_PATH) VALUES (" + to_string(num_pin) + ", " + to_string(cls) + ", " + to_string(1) + ", '" + filename + "')";
        //cout << "2-2-0  insert Query : " << query << endl;

        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            cout << "쿼리 실패" << endl;
        }
		// 해당 핀 번호에 대하여 STATE_ACTIVE를 FALSE로 변경
        query = "UPDATE LIST_PIN SET STATE_ACTIVE = 0 WHERE NUM_PIN = " + to_string(num_pin);
        //cout << "2-2-0 update Query : " << query << endl;

        // 쿼리문 실행하기
        if (mysql_query(conn, query.c_str()))
        {
            cout << "쿼리 실패" << endl;
        }
        mysql_close(conn);

        // 마이크에게 송신
        for (int i = 0; i < list_conninfo.size(); i ++)
        {
            if (list_conninfo[i].num_pin && list_conninfo[i].num_pin == num_pin)
            {
                WorkItem send_item_pin; // 응답용 WorkItem 생성
                send_item_pin.socket = list_conninfo[i].socket;
                send_item_pin.json_conv["STATE_ACTIVE"] = false;
                send_item_pin.protocol = "0-0-1";

                send_workitem(send_item_pin); // 마이크에게 송신
                break;
            }
        }
        // 관리자에게도 송신
        for (int i = 0; i < list_conninfo.size(); i++)
        {
            if (list_conninfo[i].client_id == 1)
            {
                WorkItem send_item_visor; // 응답용 WorkItem 생성
                send_item = proc_1_2_0(send_item_visor);
                send_item.socket = list_conninfo[i].socket;
                //cout << "가공완료 : " << send_item.json_conv << endl;
                //cout << "송장 : " << send_item.socket << endl;
                //cout << "protocol : " << send_item.protocol << endl;
                
                send_workitem(send_item); // 관리자에게 송신
                break;
            }
        }
    }
    else if (result == "NORMAL")
    {
        string filename = send_item.json_conv["FILE_PATH"];

        remove(("./" + filename).c_str()); // 정상 음성 파일 삭제
        //remove(filename.c_str());
    }    

    WorkItem null_item;
    return null_item; // 빈 응답 반환
}



// 프로토콜에 따라 처리 함수 호출
unordered_map<string, function<WorkItem(WorkItem& item)>> protocol_handlers =
{
    {"0-0-0", proc_0_0_0},
    {"0-0-1", proc_0_0_1},
    {"0-1-0", proc_0_1_0},

    {"1-0-0", proc_1_0_0},
    {"1-0-1", proc_1_0_1},
    {"1-0-2" ,proc_1_0_2},
    {"1-0-3" ,proc_1_0_3},
    {"1-1-0", proc_1_1_0},
    {"1-1-1", proc_1_1_1},
    {"1-1-2", proc_1_1_2},
    {"1-2-0", proc_1_2_0},
    {"1-2-1", proc_1_2_1},
    {"1-2-2", proc_1_2_2},
    {"1-2-3", proc_1_2_3},
    {"1-3-0", proc_1_3_0},
    {"1-3-1", proc_1_3_1},
    {"1-3-2", proc_1_3_2},
    {"1-3-3", proc_1_3_3},
    {"1-3-4", proc_1_3_4},
   
    {"2-0-0", proc_2_0_0},
    {"2-1-0", proc_2_1_0},
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


// @brief DB와 연결하는 함수
//@return MYSQL* conn , 연결된 myql 포인터
MYSQL* connect_db()
{
    const char* HOST = "10.10.20.111";
    const char* USER = "MACHINEEAR";
    const char* PASS = "1234";
    const char* DB = "MACHINEEAR";
    MYSQL* conn = mysql_init(nullptr);
    if (!mysql_real_connect(conn, HOST, USER, PASS, DB, 0, NULL, 0))
    {
        std::cerr << "DB 연결 실패: " << mysql_error(conn) << std::endl;
        return nullptr;
    }
    mysql_query(conn, "SET NAMES utf8mb4");

    //  UTF-8 문자셋 설정 (이모지 포함 지원)
    if (mysql_set_character_set(conn, "utf8mb4")) {
        std::cerr << "문자셋 설정 실패: " << mysql_error(conn) << std::endl;
        mysql_close(conn);
        return nullptr;
    }

    //std::cout << "DB 연결 성공 (문자셋: " << mysql_character_set_name(conn) << ")" << std::endl;
    return conn;
}



wstring Utf8ToUtf16(const std::string& utf8) 
{
    int size = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, NULL, 0);
    if (size <= 0) return L"[변환 실패]";
    std::wstring result(size, 0);
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, &result[0], size);
    result.pop_back();  // 널 문자 제거
    return result;
}

inline std::string Utf16ToUtf8(const std::wstring& utf16)
{
    int size = WideCharToMultiByte(CP_UTF8, 0, utf16.data(), (int)utf16.size(), NULL, 0, NULL, NULL);
    std::string result(size, 0);
    WideCharToMultiByte(CP_UTF8, 0, utf16.data(), (int)utf16.size(), &result[0], size, NULL, NULL);
    return result;
}









