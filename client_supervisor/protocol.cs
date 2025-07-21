using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace client_supervisor
{
    public partial class MainWindow
    {
        Dictionary<string, Action<WorkItem>> dict_proc_recv = new Dictionary<string, Action<WorkItem>>();
        Dictionary<string, Func<WorkItem, WorkItem>> dict_proc_send = new Dictionary<string, Func<WorkItem, WorkItem>>();
        private readonly object result_send = new();

        /// <summary>
        /// 수신 프로토콜에 대해서 딕셔너리에 추가
        /// </summary>
        public void AddRecvProtocol()
        {
            // 수신측 메서드 추가
            this.dict_proc_recv.Add("1-0-0", Recv_Req_Conn);
            this.dict_proc_recv.Add("1-0-1", Recv_Req_Code_Error);
            this.dict_proc_recv.Add("1-0-2", Recv_Req_Code_Anomaly);
            this.dict_proc_recv.Add("1-0-3", Recv_Req_Unreg_MAC);
            this.dict_proc_recv.Add("1-1-0", Recv_Req_Mic_List);
            this.dict_proc_recv.Add("1-2-0", Recv_Mic_State_Anomaly);
            this.dict_proc_recv.Add("1-2-2", Recv_Req_Event_List);
            this.dict_proc_recv.Add("1-3-0", Recv_Req_Map_List);
            this.dict_proc_recv.Add("1-3-3", Recv_Req_Map_Data);

            // 송신측 메서드 추가
            this.dict_proc_send.Add("1-0-0", Send_Protocol_Only);
            this.dict_proc_send.Add("1-0-1", Send_Protocol_Only);
            this.dict_proc_send.Add("1-0-2", Send_Protocol_Only);
            this.dict_proc_send.Add("1-0-3", Send_Protocol_Only);
            this.dict_proc_send.Add("1-1-0", Send_Protocol_Only);
            this.dict_proc_send.Add("1-1-1", Send_Mic_State_Change);
            this.dict_proc_send.Add("1-1-2", Send_Protocol_Only);
            this.dict_proc_send.Add("1-2-1", Send_Mic_State_Anomaly);
            this.dict_proc_send.Add("1-2-2", Send_Req_Event_List);
            this.dict_proc_send.Add("1-2-3", Send_Data_Event_List);
            this.dict_proc_send.Add("1-3-0", Send_Protocol_Only);
            this.dict_proc_send.Add("1-3-1", Send_Data_Map_List);
            this.dict_proc_send.Add("1-3-2", Send_Data_Map_Binary);
            this.dict_proc_send.Add("1-3-3", Send_Req_Map_Binary);
            this.dict_proc_send.Add("1-3-4", Send_Modify_Pin_LIst);
        }

        public async Task ExcuteCommand_Send(WorkItem item)
        {
            Console.WriteLine("---------------------------------------------------");
            if (this.dict_proc_recv.ContainsKey(item.Protocol))
            {
                // 프로토콜과 같은 키에 해당하는 메서드 실행
                await clientService.SendMessageWithHeaderAsync(this.dict_proc_send[item.Protocol](item));
                DataReceivedEventArgs response = await WaitForResponseAsync();
            }
            else
            {
                Console.WriteLine($"오류: 알 수 없는 프로토콜 '{item.Protocol}' 입니다.");
            }
        }

        public void ExcuteCommand_Recv(WorkItem item)
        {
            Console.WriteLine("---------------------------------------------------");
            //Console.WriteLine($"[Recv - Debug] Json : {item.JsonData.ToString()}");
            Console.Write($"[Recv] Protocol : {item.Protocol}");
            if(item.BinaryData.Length > 0)
            {
                Console.WriteLine($" - Binary Data Size : {item.BinaryData.Length}");
            }
            else
            {
                Console.WriteLine();
            }

            if (this.dict_proc_recv.ContainsKey(item.Protocol))
            {
                // 프로토콜과 같은 키에 해당하는 메서드 실행
                this.dict_proc_recv[item.Protocol](item);
            }
            else
            {
                Console.WriteLine($"오류: 알 수 없는 프로토콜 '{item.Protocol}' 입니다.");
            }
        }

        // 송신측 코드
        // 프로토콜만 전송
        private WorkItem Send_Protocol_Only(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-1-1 : 클라이언트(마이크) 가동 전송
        private WorkItem Send_Mic_State_Change(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-2-1 : 이상 상황이 발생한 핀에 대해서 세부사항 작성 후 서버에 전송
        private WorkItem Send_Mic_State_Anomaly(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-2-2 : 특정 요청일에 대하여 전체 기록 요청 
        private WorkItem Send_Req_Event_List(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-2-3 : 이전 기록에 대한 정보 수정 전송
        private WorkItem Send_Data_Event_List(WorkItem item)
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-3-1 : 지도 목록 수정
        private WorkItem Send_Data_Map_List(WorkItem item)
        {
            WorkItem send_item = item;

            List<JObject> list_obj = new List<JObject>();

            foreach(MapSector map in this.Map_Add)
            {
                JObject obj = new JObject();
                obj.Add("NUM_MAP", map.Num_Map);
                obj.Add("INDEX_MAP", map.Idx);
                obj.Add("NAME_MAP", map.Name);

                list_obj.Add(obj);
            }
            send_item.JsonData["MODIFIED"] = JArray.FromObject(list_obj);       // 배열화하여 넣기
            send_item.JsonData["REMOVED"] = JsonConvert.SerializeObject(list_obj);      // 직렬화하여 넣기

            return send_item;
        }
        // 1-3-2 : 지도 파일 송신
        private WorkItem Send_Data_Map_Binary(WorkItem item) 
        {
            WorkItem send_item = item;


            return send_item;
        }
        // 1-3-3 : 지도 파일 요청
        private WorkItem Send_Req_Map_Binary(WorkItem item) 
        {
            WorkItem send_item = item;
            return send_item;
        }
        // 1-3-4 : 핀 목록 수정
        private WorkItem Send_Modify_Pin_LIst(WorkItem item)
        {
            WorkItem send_item = item;

            return send_item;
        }

        // 수신측 코드
        // 1-0-0 : 접속 요청 결과 수신
        private void Recv_Req_Conn(WorkItem item)
        {
            // 서버에서 접속 거부 당했을 경우
            if (item.JsonData["RESPONSE"].ToString() == "NO")
            {
                // 연결 종료
                this.clientService.Dispose();
            }
        }
        // 1-0-1 : 고장 원인 목록 요청
        private void Recv_Req_Code_Error(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 고장 원인 목록 수신");
            this.List_Kind_Error.Clear();

            JArray arr_data = item.JsonData["DATA"] as JArray;
            foreach (JObject data in arr_data)
            {
                this.List_Kind_Error.Add(data["CODE"].Value<int>(), data["NAME"].Value<string>());
                Console.Write($"{this.List_Kind_Error.Last()} ");
            }
            Console.WriteLine();
        }
        // 1-0-2 : 이상 상태 처리 목록 요청
        private void Recv_Req_Code_Anomaly(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 이상 상태 처리 목록 수신");
            this.List_Kind_Anomaly.Clear();
            DeleteRadioButtonToPanel(wrap_kind_anomaly);

            JArray arr_data = item.JsonData["DATA"] as JArray;
            foreach (JObject data in arr_data)
            {
                this.List_Kind_Anomaly.Add(data["CODE"].Value<int>(), data["NAME"].Value<string>());
                Console.Write($"{this.List_Kind_Anomaly.Last()} ");
            }
            Console.WriteLine();

            this.CreateRadioButtons_Click(this.List_Kind_Anomaly);
        }
        // 1-0-3 : 핀 목록 테이블에 등록되지 않은 MAC 주소들 요청
        private void Recv_Req_Unreg_MAC(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 핀 목록 테이블에 등록되지 않은 MAC 주소 요청");
            this.MACList.Clear();

            JArray arr_data = item.JsonData["DATA"] as JArray;
            for (int i = 0; i < arr_data.Count; i++) {
                this.MACList.Add(arr_data[i].Value<string>());
                Console.Write($"{this.MACList.Last()} ");
            }
            Console.WriteLine();
        }
        // 1-1-0 : 클라이언트(마이크) 목록 요청, 핀 리스트
        private void Recv_Req_Mic_List(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 핀 목록 수신");

            this.mainCanvas.Children.Clear();
            this.PinList.Clear();

            JArray arr_data = item.JsonData["DATA"] as JArray;

            foreach (JObject obj_pin in arr_data)
            {
                ClientPin pin_new = new ClientPin
                {
                    Idx = obj_pin["NUM_PIN"].Value<int>(),
                    Name_Pin = obj_pin["NAME_PIN"].Value<string>(),
                    MapIndex = obj_pin["NUM_MAP"].Value<int>(),
                    Name_Location = obj_pin["NAME_LOC"].Value<string>(),
                    Name_Manager = obj_pin["NAME_MANAGER"].Value<string>(),
                    PosX = obj_pin["POS_X"].Value<double>(),
                    PosY = obj_pin["POS_Y"].Value<double>(),
                    MAC = obj_pin["MAC"].Value<string>(),
                    Date_Reg = obj_pin["DATE_REG"].Value<DateTime>(),
                    State_Anomaly = obj_pin["STATE_ANOMALY"].Value<int>(),
                    State_Active = obj_pin["STATE_ACTIVE"].Value<bool>(),
                    State_Connect = obj_pin["STATE_CONNECT"].Value<bool>()
                };

                this.PinAddToCanvas(pin_new, true);
            }

            Console.WriteLine($"핀 목록 갯수 : {this.PinList.Count}");
            foreach(ClientPin pin in this.PinList)
            {
                Console.WriteLine($"핀 이름 {pin.Name} - 번호 : {pin.Idx} - 지도번호 : {pin.MapIndex} - MAC : {pin.MAC}");
            }
        }
        // 1-2-0 : 이상상황 발생한 핀에 대하여 데이터 수신
        private void Recv_Mic_State_Anomaly(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 이상 상황 발생 데이터 수신");
        }
        // 1-2-2 : 특정 요청일에 대하여 전체 기록 수신
        private void Recv_Req_Event_List(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 특정 요청일({item.JsonData["DATE_REQ"].Value<DateTime>().ToString("d")})에 대한 로그 수신");
        }
        // 1-3-0 : 지도 목록 수신
        private void Recv_Req_Map_List(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 지도 목록 수신");

            // 기존 파일 불러오기
            this.load_maplist();

            // 서버에서 받을 데이터 목록 생성
            ObservableCollection<MapSector> mapSectors_recv = new ObservableCollection<MapSector>();

            // 서버에서 수신받은 데이터를 목록에 넣기
            JArray arr_data = item.JsonData["DATA"] as JArray;
            foreach (JObject obj_map in arr_data)
            {
                MapSector map_new = new MapSector
                {
                    Num_Map = obj_map["NUM_MAP"].Value<int>(),
                    Idx = obj_map["INDEX_MAP"].Value<int>(),
                    Name = obj_map["NAME_MAP"].Value<string>()
                };

                mapSectors_recv.Add(map_new);
            }

            // 서버에서 받은 목록에 파일이 없는 경우 삭제
            this.compare_maplist_delete(this.MapSectors, mapSectors_recv, false);

            // 파일이 없는 경우, 즉 path와 size가 없는 경우에는 파일 요청
            this.Map_Add = this.compare_maplist_add(mapSectors_recv, this.MapSectors, false);
            
            // 지도 메타데이터 저장
            this.MapSectors = mapSectors_recv;       // 수신받은 목록을 공통 사용목록에 주소 저장
            this.save_maplist();
        }
        // 1-3-3 : 요청한 지도 번호에 따른 파일 수신
        private void Recv_Req_Map_Data(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 지도 동기화 : 데이터 수신");

            //// 수신받은 데이터 메타 기록
            //MapSector map_new = new();
            //map_new.Num_Map = item.JsonData["NUM_MAP"].Value<int>();
            //map_new.Idx = item.JsonData["INDEX_MAP"].Value<int>();
            //map_new.Name = item.JsonData["NAME_MAP"].Value<string>();
            //map_new.Path = System.IO.Path.Combine(this.path_maps, item.JsonData["__META__"]["NAME"].Value<string>());
            //map_new.SizeB = item.JsonData["__META__"]["SIZE"].Value<int>();

            //// 파일 저장
            //Console.WriteLine($"파일 저장 경로 : {map_new.Path}");
            //System.IO.File.WriteAllBytes(map_new.Path, item.BinaryData);
        }
    }
}
