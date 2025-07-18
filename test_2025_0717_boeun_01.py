import socket
import json
import struct
import os
import time
######################################
import torch
from pytorch_lightning import Trainer
from model import Wavegram_AttentionMap
from data import TUTDatamodule
import pandas as pd
import os

CLASS_LABELS = [
        "fan normal", "fan abnormal",
        "pump normal", "pump abnormal",
        "slider normal", "slider abnormal",
        "valve normal", "valve abnormal",
        "gearbox normal", "gearbox abnormal",
        "ToyConveyor normal", "ToyConveyor abnormal"
    ]
CLASS = []


# 서버에게 data 보내기 함수
def send_data(sock, json_):  # 4바이트(전체크기) + 4바이트(json크기) + 실제 데이터(json)
    try:
        json_str = json.dumps(json_)
        print(f'json_str : {json_str}')
        json_bytes = json_str.encode('utf-8')  # json의 바이트 배열
        total_bytes = len(json_bytes)          # 보낼 데이터의 총 크기 (json 만 보낼 경우)
        print(f'total_bytes : {total_bytes}')
        # 4바이트로 길이 인코딩 (네트워크 바이트 오더, big-endian)
        header_total = struct.pack('>I', total_bytes)
        header_json = struct.pack('>I', len(json_bytes))   # ← 여기!
        sock.sendall(header_total + header_json + json_bytes)
        print(header_total + header_json + json_bytes)
    except Exception as e:
        print('전송 중 에러:', e)

# 서버로부터 모든 데이터가 전송되었는지 확인하는 함수
def chk_receive_data(sock, bytes_):

    buf = b'' # 바이너리로 읽음
    while len(buf) < bytes_: # 서버에게 받은 총 데이터 길이만큼 읽을 때까지 반복
        print('아직 데이터 다 안왔음')
        chunk = sock.recv(bytes_ - len(buf))
        print('1')
        if not chunk: # 연결이 끊어진 경우
            raise ConnectionError('서버와 연결이 끊어졌습니다!')
        print('2')
        buf += chunk # 데이터를 다 더함
        print('3')
    return buf

# 서버에게 받은 데이터 파싱 함수
def receive_data(sock):
    print('서버한테 데이터가 왔어요!')
    # try:
    # header 8바이트 수신하기
    header = chk_receive_data(sock, 8)  # 앞에서 8바이트 읽어서 저장 ---> header(4+4)
    print(f'header : {header}')
    total_len, json_len = struct.unpack('>II', header)  # 4바이트씩 뜯어서 ---> 전체 길이 / json 길이 저장
    print(f'총 데이터 길이(total_len) : {total_len}')
    print(f'json 길이(json_len)      : {json_len}')

    # header 뒤 실제 데이터 수신하기
    body = chk_receive_data(sock, total_len)
    print(f'body : {body}')
    # json과 data 각각 저장하기
    json_ = body[:json_len].decode("utf-8")
    if total_len > json_len: # 데이터가 있는 경우
        data_ = body[json_len:total_len]
    else: # 데이터가 없는 경우
        data_ = None


    print(f'json_data : {json_}')
    if data_ is not None:
        print(f'data : {data_}')

    return json_, data_

    # except Exception as e:
    #     print('수신 에러: ', e)
    #     return None, None

# 데이터 예측하는 함수
def pred_audio(sample_rate, duration):

    # 1. 사용할 .ckpt 파일 지정 (학습이 끝난 모델)
    ckpt_path = "best-01-1.3610.ckpt"

    # 2. 테스트 데이터셋 설정
    test_data_path = "original.wav"
    batch_size = 8
    sample_rate = 16000
    duration = 10

    # 3. DataModule 준비
    datamodule = TUTDatamodule(
        path_train="./train",  # dummy
        path_test=test_data_path,
        sample_rate=sample_rate,
        duration=duration,
        percentage_val=0.05,
        batch_size=batch_size
    )

    # 4. 모델 불러오기
    model = Wavegram_AttentionMap.load_from_checkpoint(ckpt_path, h=128, lr=0.001)
    model.eval()

    # 5. Trainer
    trainer = Trainer(accelerator="gpu", devices=1)

    # 6. 직접 test 데이터셋에서 예측 수행
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model = model.to(device)
    model.eval()

    all_results = []
    with torch.no_grad():

        # ✅ 삽입: abnormal 파일 수 확인
        # ab_count = sum(["abnormal" in f for f in datamodule.test_list])
        # print(f"[DEBUG] 테스트 파일 중 abnormal 포함 개수: {ab_count}")

        test_files = datamodule.test_list
        test_loader = datamodule.test_dataloader()
        batch_size = test_loader.batch_size

        for batch_idx, batch in enumerate(test_loader):
            x, class_label, _, _ = batch  # ✅ 정확히 class_label (0~7)를 선택
            x = x.to(device)
            class_label = class_label.to(device)

            logits_tuple = model(x, class_label)
            logits = logits_tuple[0] if isinstance(logits_tuple, tuple) else logits_tuple

            preds = torch.argmax(logits, dim=1).cpu().numpy()
            targets_cpu = class_label.cpu().numpy()

            batch_start = batch_idx * batch_size
            for i, pred_class in enumerate(preds):
                file_path = test_files[batch_start + i]

                # ✅ 삽입: 디버깅용 라벨 출력
                print(
                    f"DEBUG: pred_class = {pred_class}, target_idx = {targets_cpu[i]}, file = {os.path.basename(file_path)}")

                pred_str = CLASS_LABELS[pred_class]
                target_str = CLASS_LABELS[targets_cpu[i]]
                print(f"{os.path.basename(file_path)}\t예측: {pred_str}\t실제: {target_str}")
                all_results.append([file_path, pred_str, target_str])

    # 7. 결과 저장
    df = pd.DataFrame(all_results, columns=["file", "predicted", "target"])
    df.to_csv("test_file_predictions.csv", index=False)
    print("결과를 test_file_predictions.csv로 저장했습니다.")

    return pred_str

def main():
    IP = '10.10.20.111'
    PORT = 9000

    client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    client_socket.connect((IP, PORT))
    client_socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
    print(f'서버 {IP}:{PORT}에 연결됨')

    conn_switch = 0
    try:
        while True:
            # 접속 요청하기
            while conn_switch == 0:
                send_data(client_socket, {'PROTOCOL': '2-0-0'})  # 접속 요청 데이터 보내기
                json_, data_ = receive_data(client_socket)
                json_obj = json.loads(json_)
                print(f'json_ : {json_obj}')
                if json_obj['PROTOCOL'] == '2-0-0':
                    if json_obj['RESPONSE'] == 'NO':
                        print('서버한테 connect NO 받았어요')
                        continue
                    else:
                        print('서버한테 connect OK 받았어요')
                        conn_switch = 1

            json_, data_ = receive_data(client_socket)
            json_obj = json.loads(json_)
            protocol = json_obj['PROTOCOL']
            # 오디오 데이터 수신하기
            if protocol == '2-1-0': 
                fail_switch = 0
                if (not 'MAC' in json_obj) or (not '__META__' in json_obj):
                    fail_switch = 1
                if '__META__' in json_obj:
                    META = json_obj['__META__']
                    if (not 'SIZE' in META) or (not 'SAMPLING_RATE' in META) or (not 'TIME' in META):
                        fail_switch = 1
                if len(data_) == 0:
                    fail_switch = 1

                if fail_switch == 0: # 데이터를 정상적으로 수신한 경우
                    res_json = {'RESPONSE' : 'OK'}
                    send_data(client_socket, res_json)
                    MAC = json_obj['MAC']
                    SIZE = json_obj['__META__']['SIZE']
                    SR = json_obj['__META__']['SAMPLING_RATE']
                    TIME = json_obj['__META__']['TIME']

                    # 서버에서 받은 data_: bytes (WAV 파일) ---> 저장해야함.
                    with open("original.wav", "wb") as f:
                        f.write(data_)

                    # 예측하는 코드
                    pred_ = pred_audio(SR, TIME)
                    if pred_[-8:] == 'abnormal':
                        result = 1

                    else:
                        result = 0

                    # send_data(client_socket, result_json) # 예측 결과 보내기

                else:
                    res_json = {'RESPONSE' : 'NO'}
                    send_data(client_socket, res_json)
                    continue

            # 오디오 데이터 예측 결과 송신하고 받은 메시지
            elif protocol == '2-2-0':
                if json_['RESPONSE'] == 'OK':
                    print('예측 결과 잘 받았대요')
                else:
                    print('예측 결과 전송이 잘 안됐나봐요')

    except KeyboardInterrupt:
        print('\n사용자 중단')
    finally:
        client_socket.close()
        print('연결 종료')

if __name__ == "__main__":
    main()



