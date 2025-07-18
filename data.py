import torch
from torch.utils.data import Dataset, DataLoader
from pytorch_lightning import LightningDataModule
import librosa as lb
import numpy as np
import os
import random

# 네 기계 종류만 사용
TUT_LABELS = ["fan", "pump", "slider", "valve", "gearbox", "ToyConveyor"]

# 12-class 라벨 매핑용
CLASS_LABELS = [
    "fan normal", "fan abnormal",
    "pump normal", "pump abnormal",
    "slider normal", "slider abnormal",
    "valve normal", "valve abnormal",
    "gearbox normal", "gearbox abnormal",
    "ToyConveyor normal", "ToyConveyor abnormal"
]

class TUTDataset(Dataset):
    def __init__(self, path_data, list_files, sample_rate, duration):
        self.sample_rate = sample_rate
        self.path_data = path_data
        self.list_files = list_files
        self.duration = duration

    def __getitem__(self, index):
        file_path = self.list_files[index]

        # Load audio
        audio_data, _ = lb.load(file_path, sr=self.sample_rate, res_type="polyphase")
        if len(audio_data) > int(self.duration * self.sample_rate):
            audio_data = audio_data[:int(self.duration * self.sample_rate)]

        # machine_type: 0 ~ 3
        machine_type = None
        for i, name_class in enumerate(TUT_LABELS):
            if name_class in file_path:
                machine_type = i
                break

        # status_type: 0 = normal, 1 = abnormal
        # status_type = 0 if "normal" in file_path.lower() else 1
        status_type = 1 if "abnormal" in file_path.lower() else 0

        # 최종 class_label: 0 ~ 7
        class_label = machine_type * 2 + status_type
        print(f'file_name : {file_path}')
        print(f'class_label : {class_label}')
        return audio_data, class_label, status_type, class_label

    def __len__(self):
        return len(self.list_files)

class TUTDatamodule(LightningDataModule):
    def __init__(self, path_train, path_test, sample_rate, duration, percentage_val, batch_size):
        super().__init__()
        self.path_train = path_train
        self.path_test = path_test
        self.sample_rate = sample_rate
        self.duration = duration
        self.percentage_val = percentage_val
        self.batch_size = batch_size

        self.train_list = self.scan_all_dir(self.path_train)
        self.val_list = random.sample(self.train_list, int(len(self.train_list) * percentage_val))
        self.train_list = list(set(self.train_list) - set(self.val_list))
        self.test_list = self.scan_all_dir(self.path_test)

    def scan_all_dir(self, path):
        list_all_files = []
        for root, dirs, files in os.walk(path):
            for file in files:
                if file.endswith(".wav"):
                    list_all_files.append(os.path.join(root, file))
        return list_all_files

    def setup(self, stage=None):
        pass

    def prepare_data(self):
        pass

    def train_dataloader(self):
        dataset = TUTDataset(self.path_train, self.train_list, self.sample_rate, self.duration)
        return DataLoader(dataset, batch_size=self.batch_size, shuffle=True)

    def val_dataloader(self):
        dataset = TUTDataset(self.path_train, self.val_list, self.sample_rate, self.duration)
        return DataLoader(dataset, batch_size=self.batch_size, shuffle=False)

    def test_dataloader(self):
        dataset = TUTDataset(self.path_test, self.test_list, self.sample_rate, self.duration)
        return DataLoader(dataset, batch_size=self.batch_size, shuffle=False)
