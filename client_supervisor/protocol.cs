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
            this.dict_proc_send.Add("1-1-1", Send_Protocol_Only);
            this.dict_proc_send.Add("1-1-2", Send_Protocol_Only);
            this.dict_proc_send.Add("1-2-0", Send_Protocol_Only);
            this.dict_proc_send.Add("1-2-1", Send_Mic_State_Anomaly);
            this.dict_proc_send.Add("1-2-2", Send_Protocol_Only);
            this.dict_proc_send.Add("1-2-3", Send_Data_Event_List);
            this.dict_proc_send.Add("1-3-0", Send_Protocol_Only);
            this.dict_proc_send.Add("1-3-1", Send_Data_Map_List);
            this.dict_proc_send.Add("1-3-2", Send_Protocol_Only);
            this.dict_proc_send.Add("1-3-3", Send_Req_Map_Binary);
            this.dict_proc_send.Add("1-3-4", Send_Modify_Pin_LIst);
        }
        public void Act_SendAndRecv(DataReceivedEventArgs e)
        {
            WorkItem receivedWorkItem = new WorkItem
            {
                Protocol = e.Protocol,
                JsonData = e.JsonData,
                BinaryData = e.BinaryData
            };

            this.ExcuteCommand_Recv(receivedWorkItem);
        }
        private async Task<DataReceivedEventArgs> ExcuteCommand_SendAndWait(WorkItem item, string responseProtocol, int timeoutMs = 5000)
        {
            Console.WriteLine("---------------------------------------------------");
            if (clientService == null || !clientService.IsConnected)
            {
                throw new InvalidOperationException("클라이언트 서비스가 초기화되지 않았거나 서버에 연결되어 있지 않습니다.");
            }
            return await clientService.SendMessageAndWaitForResponseAsync(item, responseProtocol, timeoutMs);
        }
        public async Task ExcuteCommand_Send(WorkItem item)
        {
            Console.WriteLine("---------------------------------------------------");
            if (this.dict_proc_send.ContainsKey(item.Protocol))
            {
                // 프로토콜과 같은 키에 해당하는 메서드 실행
                await clientService.SendMessageWithHeaderAsync(this.dict_proc_send[item.Protocol](item));
            }
            else
            {
                Console.WriteLine($"오류: 알 수 없는 프로토콜 '{item.Protocol}' 입니다.");
            }
        }

        public void ExcuteCommand_Recv(WorkItem item)
        {
            Console.WriteLine("---------------------------------------------------");
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
        // 1-2-1 : 이상 상황이 발생한 핀에 대해서 세부사항 작성 후 서버에 전송
        private WorkItem Send_Mic_State_Anomaly(WorkItem item)
        {
            WorkItem send_item = item;

            send_item.JsonData["NUM_EVENT"] = this.CurrentClickedAnomaly.Idx;
            send_item.JsonData["CODE_ANOMALY"] = this.CurrentClickedAnomaly.Code_Anomaly;
            send_item.JsonData["MANAGER_PROC"] = this.CurrentClickedAnomaly.Worker;
            send_item.JsonData["MEMO"] = this.CurrentClickedAnomaly.Memo;

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

            // 2. 수정된 맵 정보 처리 (MODIFIED)
            List<JObject> modifiedMaps = new List<JObject>();
            foreach (MapSector map in this.Map_Modified)
            {
                JObject obj = new JObject();
                obj.Add("NUM_MAP", map.Num_Map);
                obj.Add("INDEX_MAP", map.Idx);
                obj.Add("NAME_MAP", map.Name_Map);
                obj.Add("PATH_LOCAL", map.Path);
                obj.Add("SIZE_MAP", map.SizeB);
                modifiedMaps.Add(obj);
            }
            send_item.JsonData["MODIFIED"] = JArray.FromObject(modifiedMaps); // 수정된 맵은 "MODIFIED" 키로 전송
            send_item.JsonData["REMOVED"] = JArray.FromObject(this.Map_Removed); // 삭제된 맵의 Idx 리스트를 "REMOVED" 키로 전송

            return send_item;
        }
        private WorkItem Send_Data_Map_Binary(WorkItem item)
        {
            WorkItem send_item = item;

            List<JObject> addedMaps = new List<JObject>();
            foreach (MapSector map in this.Map_Add)
            {
                JObject obj = new JObject();
                obj.Add("NUM_MAP", map.Num_Map);
                obj.Add("INDEX_MAP", map.Idx);
                obj.Add("NAME_MAP", map.Name_Map);
                obj.Add("PATH_LOCAL", map.Path);
                obj.Add("SIZE_MAP", map.SizeB);

                addedMaps.Add(obj);
            }
            send_item.JsonData["ADD"] = JArray.FromObject(addedMaps); // 추가된 맵은 "ADD" 키로 전송

            // 2. 수정된 맵 정보 처리 (MODIFIED)
            List<JObject> modifiedMaps = new List<JObject>();
            foreach (MapSector map in this.Map_Modified)
            {
                JObject obj = new JObject();
                obj.Add("NUM_MAP", map.Num_Map);
                obj.Add("INDEX_MAP", map.Idx);
                obj.Add("NAME_MAP", map.Name_Map);
                obj.Add("PATH_LOCAL", map.Path);
                obj.Add("SIZE_MAP", map.SizeB);
                modifiedMaps.Add(obj);
            }
            send_item.JsonData["MODIFIED"] = JArray.FromObject(modifiedMaps); // 수정된 맵은 "MODIFIED" 키로 전송
            send_item.JsonData["REMOVED"] = JArray.FromObject(this.Map_Removed); // 삭제된 맵의 Idx 리스트를 "REMOVED" 키로 전송

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

            List<object> list_arr = new List<object>();

            // 추가 부분 생성
            foreach( ClientPin pin in this.PinList_Add)
            {
                Dictionary<string, object> dict_arr = new Dictionary<string, object>();
      
                dict_arr.Add("NUM_PIN", pin.Idx);
                dict_arr.Add("NUM_MAP", this.MapSectors[this.idx_map].Num_Map);
                dict_arr.Add("NAME_PIN", pin.Name_Pin);
                dict_arr.Add("NAME_LOC", pin.Name_Location);
                dict_arr.Add("NAME_MANAGER", pin.Name_Manager);
                dict_arr.Add("MAC", pin.MAC);
                dict_arr.Add("POS_X", pin.PosX);
                dict_arr.Add("POS_Y", pin.PosY);

                list_arr.Add(dict_arr);
            }
            send_item.JsonData["ADD"] = JArray.FromObject(list_arr);

            // 수정 부분 생성
            list_arr.Clear();
            foreach (ClientPin pin in this.PinList_Modified)
            {
                Dictionary<string, object> dict_arr = new Dictionary<string, object>();

                dict_arr.Add("NUM_PIN", pin.Idx);
                dict_arr.Add("NAME_PIN", pin.Name_Pin);
                dict_arr.Add("NAME_LOC", pin.Name_Location);
                dict_arr.Add("NAME_MANAGER", pin.Name_Manager);

                list_arr.Add(dict_arr);
            }
            send_item.JsonData["MODIFIED"] = JArray.FromObject(list_arr);

            // 삭제 부분 생성
            list_arr.Clear();
            foreach (int num_pin in this.PinList_Remove)
            {
                list_arr.Add(num_pin);
            }
            send_item.JsonData["REMOVED"] = JArray.FromObject(list_arr);

            return send_item;
        }

        // 수신측 코드
        // 1-0-0 : 접속 요청 결과 수신
        private void Recv_Req_Conn(WorkItem item)
        {
            this.List_Daily_Anomaly.Clear();
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
        private void Recv_Req_Mic_List(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 핀 목록 수신");

            JArray arr_data = item.JsonData["DATA"] as JArray;

            for(int i = 0; i < this.PinList.Count; i++)
            {
                if (this.PinList[i].Idx == 0) this.PinList.RemoveAt(i);
            }

            // 1. 새로 수신된 핀 데이터를 임시 리스트에 파싱
            List<ClientPin> newPins = new List<ClientPin>();
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
                    State_Active = obj_pin["STATE_ACTIVE"].Value<bool>(),
                    State_Connect = obj_pin["STATE_CONNECT"].Value<bool>()
                };
                newPins.Add(pin_new);
            }

            // 2. 삭제된 핀 처리
            // 기존 PinList에서 newPins에 없는 핀들을 찾아서 제거
            var pinsToRemove = this.PinList.Where(existingPin => !newPins.Any(newPin => newPin.Equals(existingPin))).ToList();
            foreach (ClientPin pinToRemove in pinsToRemove)
            {
                Console.WriteLine($"[삭제] 핀: {pinToRemove.Name_Pin} ({pinToRemove.MAC})");
                this.PinList.Remove(pinToRemove); // ObservableCollection에서 제거 -> UI도 자동으로 업데이트됨 (만약 바인딩되어 있다면)
                mainCanvas.Children.Remove(pinToRemove); // Canvas에서 UI 요소 직접 제거
            }

            // 3. 추가되거나 변경된 핀 처리
            foreach (ClientPin newPin in newPins)
            {
                // 기존 PinList에서 동일한 MAC을 가진 핀을 찾음
                ClientPin? existingPin = this.PinList.FirstOrDefault(p => p.MAC == newPin.MAC);

                if (existingPin == null)
                {
                    // 추가된 핀: PinList에 없고 새로운 목록에 있는 핀
                    Console.WriteLine($"[추가] 핀: {newPin.Name_Pin} ({newPin.MAC})");
                    this.PinList.Add(newPin); // ObservableCollection에 추가
                    PinAddToCanvas(newPin, false); // Canvas에 UI 요소 추가 (PinList에는 이미 추가했으므로 false)
                }
                else
                {
                    // 변경된 핀: PinList에도 있고 새로운 목록에도 있는 핀. 변경사항이 있는지 확인.
                    if (existingPin.HasChanged(newPin))
                    {
                        Console.WriteLine($"[변경] 핀: {existingPin.Name_Pin} ({existingPin.MAC})");
                        // 기존 핀의 속성 업데이트
                        existingPin.Idx = newPin.Idx;
                        existingPin.Name_Pin = newPin.Name_Pin;
                        existingPin.MapIndex = newPin.MapIndex;
                        existingPin.Name_Location = newPin.Name_Location;
                        existingPin.Name_Manager = newPin.Name_Manager;
                        existingPin.PosX = newPin.PosX;
                        existingPin.PosY = newPin.PosY;
                        existingPin.Date_Reg = newPin.Date_Reg;
                        // 상태 속성 업데이트는 INotifyPropertyChanged에 의해 색상 변경까지 자동으로 트리거
                        existingPin.State_Active = newPin.State_Active;
                        existingPin.State_Connect = newPin.State_Connect;
                    }
                    // 변경 사항이 없는 핀은 아무것도 하지 않음
                }

                // 색상 변경
                if (newPin.State_Connect == false)
                {
                    newPin.ChangeColorMode(STATE_COLOR.OFFLINE);
                }
                else
                {
                    bool flag_anomaly = false;

                    // 이상 내역에 존재할 때
                    for (int j = 0; j < this.List_Daily_Anomaly.Count; j++)
                    {
                        // 최신으로부터 수신하여 현재 연결된 핀과 같은 핀이 존재할 경우, 색상 변경
                        if (this.List_Daily_Anomaly[j].Pin.Idx == newPin.Idx)
                        {
                            if (this.List_Daily_Anomaly[j].Code_Anomaly == 1)
                            {
                                newPin.ChangeColorMode(STATE_COLOR.ANOMALY);
                                flag_anomaly = true;
                            }
                            break;
                        }
                    }

                    // 이상이 없을때만 대기, 작업 색상 업데이트
                    if(flag_anomaly == false)
                    {
                        if (newPin.State_Active == false)
                        {
                            newPin.ChangeColorMode(STATE_COLOR.STANDBY);
                        }
                        else
                        {
                            newPin.ChangeColorMode(STATE_COLOR.WORKING);
                        }
                    }
                }
            }

            Console.WriteLine($"최종 핀 목록 갯수 : {this.PinList.Count}");
            foreach (ClientPin pin in this.PinList)
            {
                Console.WriteLine($"핀 이름 : {pin.Name_Pin} - 번호 : {pin.Idx} - 지도번호 : {pin.MapIndex} - MAC : {pin.MAC} - Connect : {pin.State_Connect} - Active : {pin.State_Active}");
            }
        }
        // 1-2-0 : 이상상황 발생한 핀에 대하여 데이터 수신, 클라이언트가 요청하면 일일 전체 목록 요청 후 수신(처음 접속할 때만 요청)
        private void Recv_Mic_State_Anomaly(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 이상 상황 발생 데이터 수신");

            JArray arr_data = item.JsonData["DATA"] as JArray;
            Console.WriteLine($"Raw Json : {item.JsonData}");

            foreach (JObject obj_ano in arr_data)
            {
                // 핀 목록에서 같은 인덱스를 가진 핀에 대해서 얕은 복사
                ClientPin pin_new = new ClientPin();
                foreach( ClientPin pin in this.PinList)
                {
                    if (pin.Idx == obj_ano["NUM_PIN"].Value<int>()) pin_new = pin;
                }

                string name_map = "";
                foreach (MapSector map in this.MapSectors)
                {
                    if (map.Num_Map == pin_new.MapIndex) name_map = map.Name_Map;
                }

                int idx = obj_ano["NUM_EVENT"].Value<int>(); // 이벤트 번호
                int code_error = obj_ano["CODE_ERROR"].Value<int>(); // 이상 코드
                int code_anomaly = obj_ano["CODE_ANOMALY"].Value<int>(); // 상태 코드
                string worker = obj_ano["MANAGER_PROC"].Value<string>(); // 처리자 이름
                string memo = obj_ano["MEMO"].Value<string>(); // 메모 내용

                DateTime time_start = DateTime.MinValue; // 기본값 설정
                if (obj_ano.TryGetValue("DATE_START", out JToken dateStartToken) && dateStartToken.Type != JTokenType.Null)
                {
                    DateTime.TryParse(dateStartToken.ToString(), out time_start);
                }

                DateTime time_end = DateTime.MinValue; // 기본값 설정
                if (obj_ano.TryGetValue("DATE_END", out JToken dateEndToken) && dateEndToken.Type != JTokenType.Null)
                {
                    DateTime.TryParse(dateEndToken.ToString(), out time_end);
                }

                // 중복검사
                AnomalyLog newLogEntry = new AnomalyLog(idx, pin_new, time_start, code_error, name_map, worker, time_end, memo, code_anomaly);
                bool isDuplicateIdx = this.List_Daily_Anomaly.Any(log => log.Idx == newLogEntry.Idx);

                if (!isDuplicateIdx) // 중복된 Idx가 없는 경우에만 추가
                {
                    this.List_Daily_Anomaly.Insert(0, newLogEntry);
                    newLogEntry.Name_Loc = pin_new.Name_Location;

                    foreach (var val in this.List_Kind_Error)
                    {
                        if (val.Key == code_error)
                        {
                            newLogEntry.Str_Error = val.Value;
                            break; // 찾았으면 더 이상 반복할 필요가 없으므로 루프 종료
                        }
                    }
                }
            }

            // 핀 색상 업데이트
            for (int i = 0; i < this.PinList.Count; i++)
            {
                // 온라인 상태가 아니면 패스
                if (this.PinList[i].State_Connect == false) continue;

                for (int j = 0; j < this.List_Daily_Anomaly.Count; j++)
                {
                    // 최신으로부터 수신하여 현재 연결된 핀과 같은 핀이 존재할 경우, 색상 변경
                    if (this.List_Daily_Anomaly[j].Pin.Idx == this.PinList[i].Idx){
                        if (this.List_Daily_Anomaly[j].Code_Anomaly == 1)
                        {
                            this.PinList[i].ChangeColorMode(STATE_COLOR.ANOMALY);
                            this.PinList[i].State_Active = false;
                        }
                        else
                        {
                            this.PinList[i].ChangeColorMode(STATE_COLOR.STANDBY);
                        }
                        break;
                    }
                }
            }

            int idx_sorted = 0;
            foreach (MapSector map in this.MapSectors)
            {
                if (map.Idx == this.idx_map)
                {
                    idx_sorted = map.Idx - 1;
                }
            }

            // 핀 보임 여부 설정
            foreach (var child in mainCanvas.Children)
            {
                if (child is ClientPin pin)
                {
                    pin.Visibility = pin.MapIndex == this.MapSectors[idx_sorted].Num_Map ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        // 1-2-2 : 특정 요청일에 대하여 전체 기록 수신
        private void Recv_Req_Event_List(WorkItem item)
        {
            // 서버에서 받을 데이터 목록 생성
            ObservableCollection<AnomalyLog> anomaly_logs = new ObservableCollection<AnomalyLog>();

            // 서버에서 수신받은 데이터를 목록에 넣기
            JArray? arr_data = item.JsonData["DATA"] as JArray;

            if(arr_data != null && arr_data.Count > 0)
            {
                foreach (JObject obj_ano in arr_data)
                {
                    ClientPin pin_new = new ClientPin // 핀 정보
                    {
                        Idx = obj_ano["NUM_PIN"].Value<int>(),
                        Name_Pin = obj_ano["NAME_PIN"].Value<string>(),
                        Name_Location = obj_ano["NAME_LOC"].Value<string>(),
                        Name_Manager = obj_ano["NAME_MANAGER"].Value<string>()
                    };
                    string name_map = obj_ano["NAME_MAP"].Value<string>();
                    int idx = obj_ano["NUM_EVENT"].Value<int>(); // 이벤트 번호
                    int code_error = obj_ano["CODE_ERROR"].Value<int>(); // 이상 코드
                    int code_anomaly = obj_ano["CODE_ANOMALY"].Value<int>(); // 상태 코드
                    string worker = obj_ano["MANAGER_PROC"].Value<string>(); // 처리자 이름
                    string memo = obj_ano["MEMO"].Value<string>(); // 메모 내용

                    DateTime time_start = DateTime.MinValue; // 기본값 설정
                    if (obj_ano.TryGetValue("DATE_START", out JToken dateStartToken) && dateStartToken.Type != JTokenType.Null)
                    {
                        if (!DateTime.TryParse(dateStartToken.ToString(), out time_start))
                        {
                            // 파싱 실패 시, time_start는 DateTime.MinValue로 유지되거나 원하는 다른 기본값 설정
                            Console.WriteLine($"[경고] DATE_START '{dateStartToken}' 파싱 실패! DateTime.MinValue로 설정됩니다.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[정보] DATE_START 키가 없거나 값이 null입니다. DateTime.MinValue로 설정됩니다.");
                    }

                    DateTime time_end = DateTime.MinValue; // 기본값 설정
                    if (obj_ano.TryGetValue("DATE_END", out JToken dateEndToken) && dateEndToken.Type != JTokenType.Null)
                    {
                        if (!DateTime.TryParse(dateEndToken.ToString(), out time_end))
                        {
                            // 파싱 실패 시, time_end는 DateTime.MinValue로 유지되거나 원하는 다른 기본값 설정
                            Console.WriteLine($"[경고] DATE_END '{dateEndToken}' 파싱 실패! DateTime.MinValue로 설정됩니다.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[정보] DATE_END 키가 없거나 값이 null입니다. DateTime.MinValue로 설정됩니다.");
                    }

                    anomaly_logs.Add(new AnomalyLog(idx, pin_new, time_start, code_error, name_map, worker, time_end, memo, code_anomaly));
                }

                this.log_total.transit_log(anomaly_logs);
            }
            else
            {
                MessageBox.Show("해당 날짜에 대한 기록이 없습니다.", "정보", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        // 1-3-0 : 지도 목록 수신
        private void Recv_Req_Map_List(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 지도 목록 수신");

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
                    Name_Map = obj_map["NAME_MAP"].Value<string>(),
                    SizeB = obj_map["SIZE_MAP"].Value<int>(),
                    Path = obj_map["PATH_LOCAL"].Value<string>()
                };

                mapSectors_recv.Add(map_new);
            }

            Console.WriteLine();
            Console.WriteLine("비교전");
            foreach (MapSector map in this.MapSectors)
            {
                Console.WriteLine($"Num : {map.Num_Map} - Path : {map.Path} - Size : {map.SizeB}");
            }

            // 서버에서 받은 목록에 파일이 없는 경우 삭제
            this.Compare_Maplist(this.MapSectors, mapSectors_recv);

            Console.WriteLine();
            Console.WriteLine("추가전");
            foreach (MapSector map in this.MapSectors)
            {
                Console.WriteLine($"Num : {map.Num_Map} - Path : {map.Path} - Size : {map.SizeB}");
            }

            // 파일이 없는 경우, 즉 path와 size가 없는 경우에는 파일 요청
            this.Add_Maplist(this.MapSectors, mapSectors_recv, false);

            Console.WriteLine();
            Console.WriteLine("추가후");
            foreach (MapSector map in this.MapSectors)
            {
                Console.WriteLine($"Num : {map.Num_Map} - Path : {map.Path} - Size : {map.SizeB}");
            }

            this.MapSectors = mapSectors_recv;

            // NUM_MAP이 다 0이므로 새롭게 다시 배정
            this.resign_num_map(this.MapSectors, mapSectors_recv);

            Console.WriteLine();
            Console.WriteLine("재할당후");
            foreach (MapSector map in this.MapSectors)
            {
                Console.WriteLine($"Num : {map.Num_Map} - Path : {map.Path} - Size : {map.SizeB}");
            }

        }
        // 1-3-3 : 요청한 지도 번호에 따른 파일 수신
        private void Recv_Req_Map_Data(WorkItem item)
        {
            Console.WriteLine($"[{item.Protocol}] 지도 동기화 : 데이터 수신");

            // 수신받은 데이터 메타 기록
            MapSector map_new = new();
            map_new.Num_Map = item.JsonData["NUM_MAP"].Value<int>();
            map_new.Idx = item.JsonData["INDEX_MAP"].Value<int>();
            map_new.Name_Map = item.JsonData["NAME_MAP"].Value<string>();
            map_new.Path = System.IO.Path.Combine(this.path_maps, item.JsonData["__META__"]["NAME"].Value<string>());
            map_new.SizeB = item.JsonData["__META__"]["SIZE"].Value<int>();
            map_new.SizeB = item.JsonData["__META__"]["SIZE"].Value<int>();

            // 파일 저장
            Console.WriteLine($"파일 저장 경로 : {map_new.Path}");
            System.IO.File.WriteAllBytes(map_new.Path, item.BinaryData);

            // 경로 업데이트, 같은 지도번호에 대한 경로 업데이트
            for (int i = 0; i < this.MapSectors.Count; i++)
            {
                if (this.MapSectors[i].Num_Map == map_new.Num_Map)
                {
                    this.MapSectors[i].Path = map_new.Path;
                    break;
                }
            }

            this.save_maplist();
        }
    }
}
