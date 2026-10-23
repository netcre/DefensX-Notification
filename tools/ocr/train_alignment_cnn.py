from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

import numpy as np
import torch
from torch import nn

import train_primary_crnn as primary


SEED = 20260816
ROOT = Path(__file__).resolve().parents[2]
ARTIFACT_DIR = ROOT / "artifacts"


class AlignmentCnn(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv1d(2, 16, 9, padding=4),
            nn.ReLU(),
            nn.MaxPool1d(2),
            nn.Conv1d(16, 32, 7, padding=3),
            nn.ReLU(),
            nn.MaxPool1d(2),
            nn.Conv1d(32, 64, 5, padding=2),
            nn.ReLU(),
            nn.AdaptiveMaxPool1d(30),
        )
        self.classifier = nn.Sequential(
            nn.Flatten(),
            nn.Linear(64 * 30, 128),
            nn.ReLU(),
            nn.Linear(128, 50),
        )

    def forward(self, projections: torch.Tensor) -> torch.Tensor:
        return self.classifier(self.features(projections))


def read_primary_model(path: Path) -> primary.CaptchaCnn:
    model = primary.CaptchaCnn()
    state = {}
    with path.open("rb") as stream:
        if stream.read(9) != b"DWCMODEL1":
            raise ValueError("Invalid primary model header")
        tensor_count = struct.unpack("<i", stream.read(4))[0]
        for _ in range(tensor_count):
            name_length = struct.unpack("<i", stream.read(4))[0]
            name = stream.read(name_length).decode("utf-8")
            rank = struct.unpack("<i", stream.read(4))[0]
            shape = struct.unpack(f"<{rank}i", stream.read(rank * 4))
            count = math.prod(shape)
            values = np.frombuffer(
                stream.read(count * 4),
                dtype="<f4",
            ).reshape(shape).copy()
            state[name] = torch.from_numpy(values)
    model.load_state_dict(state)
    model.eval()
    return model


def project_masks(masks: np.ndarray) -> torch.Tensor:
    opened = np.asarray(
        [primary.remove_thin_lines(mask) for mask in masks],
        dtype=np.float32,
    )
    projections = np.stack(
        (masks.sum(axis=1), opened.sum(axis=1)),
        axis=1,
    )
    return torch.from_numpy(projections / primary.HEIGHT)


def make_synthetic_batch(
    font: np.ndarray,
    rng: np.random.Generator,
    batch_size: int,
) -> tuple[torch.Tensor, torch.Tensor]:
    masks = []
    targets = []
    for _ in range(batch_size):
        code = "".join(str(value) for value in rng.integers(0, 10, primary.LENGTH))
        mask, start_x = primary.render(code, font, rng, with_interference=True)
        masks.append(mask.astype(np.float32))
        targets.append(start_x - 16)
    return project_masks(np.asarray(masks)), torch.tensor(targets)


def evaluate_alignment(
    model: AlignmentCnn,
    projections: torch.Tensor,
    targets: torch.Tensor,
) -> tuple[int, int]:
    model.eval()
    with torch.no_grad():
        predictions = model(projections).argmax(dim=1)
    exact = int((predictions == targets).sum())
    within_two = int(((predictions - targets).abs() <= 2).sum())
    return exact, within_two


def write_model(model: AlignmentCnn, path: Path) -> str:
    state = model.state_dict()
    with path.open("wb") as stream:
        stream.write(b"DWCALIGN1")
        stream.write(struct.pack("<i", len(state)))
        for name, tensor in state.items():
            name_bytes = name.encode("utf-8")
            values = tensor.detach().cpu().contiguous().numpy().astype("<f4")
            stream.write(struct.pack("<i", len(name_bytes)))
            stream.write(name_bytes)
            stream.write(struct.pack("<i", values.ndim))
            stream.write(struct.pack(f"<{values.ndim}i", *values.shape))
            stream.write(values.tobytes())
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--epochs", type=int, default=18)
    parser.add_argument("--steps-per-epoch", type=int, default=80)
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--real-fine-tune-steps", type=int, default=400)
    parser.add_argument("--online-sample-dir", type=Path, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    torch.set_num_threads(8)
    torch.manual_seed(SEED)
    np.random.seed(SEED)
    font = primary.load_digit_font()
    model = AlignmentCnn()
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.001, weight_decay=1e-5)
    criterion = nn.CrossEntropyLoss()
    rng = np.random.default_rng(SEED)

    for epoch in range(args.epochs):
        model.train()
        total_loss = 0.0
        for _ in range(args.steps_per_epoch):
            projections, targets = make_synthetic_batch(font, rng, args.batch_size)
            loss = criterion(model(projections), targets)
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            optimizer.step()
            total_loss += float(loss.detach())
        validation_rng = np.random.default_rng(SEED + 1000 + epoch)
        validation_projections, validation_targets = make_synthetic_batch(
            font,
            validation_rng,
            500,
        )
        exact, within_two = evaluate_alignment(
            model,
            validation_projections,
            validation_targets,
        )
        print(
            f"epoch={epoch + 1} loss={total_loss / args.steps_per_epoch:.4f} "
            f"exact={exact}/500 within2={within_two}/500",
            flush=True,
        )

    primary_model = read_primary_model(ARTIFACT_DIR / "captcha_primary_cnn.bin")
    fixed_masks = primary.load_real_masks()[:, 0].numpy()
    fixed_labels = list(primary.LABELS)
    online_masks, online_labels = primary.load_online_samples(args.online_sample_dir)
    fixed_alignments = primary.choose_real_alignments(
        primary_model,
        fixed_masks,
        fixed_labels,
    )
    online_alignments = primary.choose_real_alignments(
        primary_model,
        online_masks,
        online_labels,
    )
    holdout_indices = np.arange(4, len(online_labels), 5)
    training_indices = np.setdiff1d(np.arange(len(online_labels)), holdout_indices)
    training_masks = np.concatenate((fixed_masks, online_masks[training_indices]))
    training_targets = np.asarray(
        [*fixed_alignments, *(online_alignments[index] for index in training_indices)],
        dtype=np.int64,
    ) - 16
    holdout_projections = project_masks(online_masks[holdout_indices])
    holdout_targets = torch.tensor(
        [online_alignments[index] - 16 for index in holdout_indices]
    )

    optimizer = torch.optim.AdamW(model.parameters(), lr=0.0001, weight_decay=1e-5)
    real_rng = np.random.default_rng(SEED + 2000)
    for step in range(args.real_fine_tune_steps):
        if step % 3:
            indices = real_rng.integers(0, len(training_targets), args.batch_size)
            projections = project_masks(training_masks[indices])
            targets = torch.from_numpy(training_targets[indices])
        else:
            projections, targets = make_synthetic_batch(
                font,
                real_rng,
                args.batch_size,
            )
        model.train()
        loss = criterion(model(projections), targets)
        optimizer.zero_grad(set_to_none=True)
        loss.backward()
        optimizer.step()
        if (step + 1) % 100 == 0:
            exact, within_two = evaluate_alignment(
                model,
                holdout_projections,
                holdout_targets,
            )
            print(
                f"real_step={step + 1} loss={float(loss.detach()):.4f} "
                f"holdout_exact={exact}/{len(holdout_targets)} "
                f"holdout_within2={within_two}/{len(holdout_targets)}",
                flush=True,
            )

    ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
    output_path = ARTIFACT_DIR / "captcha_alignment_cnn.bin"
    weights_sha256 = write_model(model, output_path)
    metadata = {
        "format": "DWCALIGN1",
        "architecture": "Conv1d16-Conv1d32-Conv1d64-Pool30-FC128-FC50",
        "input": "raw and thin-line-opened 240-column foreground projections",
        "target": "captcha start x in inclusive range 16..65",
        "weights_sha256": weights_sha256,
        "synthetic_training_seed": SEED,
        "epochs": args.epochs,
        "steps_per_epoch": args.steps_per_epoch,
        "batch_size": args.batch_size,
        "real_fine_tune_steps": args.real_fine_tune_steps,
        "real_training_samples": int(len(training_targets)),
        "real_holdout_samples": int(len(holdout_targets)),
    }
    (ARTIFACT_DIR / "captcha_alignment_cnn.json").write_text(
        json.dumps(metadata, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"model_sha256={weights_sha256}")


if __name__ == "__main__":
    main()
