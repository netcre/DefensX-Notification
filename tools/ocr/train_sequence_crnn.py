from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

import numpy as np
import torch
from torch import nn

import train_primary_crnn as primary
from train_alignment_cnn import read_primary_model


SEED = 20260817
BLANK = 10
TIME_STEPS = 60
ROOT = Path(__file__).resolve().parents[2]
ARTIFACT_DIR = ROOT / "artifacts"


class CaptchaSequenceCnn(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv2d(2, 24, 3, padding=1),
            nn.ReLU(),
            nn.MaxPool2d((2, 2)),
            nn.Conv2d(24, 48, 3, padding=1),
            nn.ReLU(),
            nn.MaxPool2d((2, 2)),
            nn.Conv2d(48, 96, 3, padding=1),
            nn.ReLU(),
            nn.MaxPool2d((2, 1)),
            nn.Conv2d(96, 128, 3, padding=1),
            nn.ReLU(),
            nn.MaxPool2d((2, 1)),
            nn.AdaptiveMaxPool2d((1, TIME_STEPS)),
        )
        self.decoder = nn.Sequential(
            nn.Conv1d(128, 128, 5, padding=2),
            nn.ReLU(),
            nn.Conv1d(128, 11, 1),
        )

    def forward(self, images: torch.Tensor) -> torch.Tensor:
        features = self.features(images).squeeze(2)
        return self.decoder(features)


def build_channels(masks: np.ndarray) -> torch.Tensor:
    opened = np.asarray(
        [primary.remove_thin_lines(mask) for mask in masks],
        dtype=np.float32,
    )
    return torch.from_numpy(np.stack((masks, opened), axis=1))


def make_synthetic_batch(
    font: np.ndarray,
    rng: np.random.Generator,
    batch_size: int,
) -> tuple[torch.Tensor, list[str], np.ndarray]:
    masks = []
    labels = []
    starts = []
    for _ in range(batch_size):
        label = "".join(str(value) for value in rng.integers(0, 10, primary.LENGTH))
        mask, start_x = primary.render(label, font, rng, with_interference=True)
        masks.append(mask.astype(np.float32))
        labels.append(label)
        starts.append(start_x)
    return build_channels(np.asarray(masks)), labels, np.asarray(starts)


def calculate_loss(
    model: CaptchaSequenceCnn,
    images: torch.Tensor,
    labels: list[str],
    starts: np.ndarray,
    criterion: nn.CrossEntropyLoss,
) -> torch.Tensor:
    logits = model(images)
    targets = torch.full((len(labels), TIME_STEPS), BLANK, dtype=torch.long)
    for row, (label, start_x) in enumerate(zip(labels, starts)):
        for position, character in enumerate(label):
            center = round((int(start_x) + position * 26 + 11) / 4)
            left = max(0, center - 1)
            right = min(TIME_STEPS, center + 2)
            targets[row, left:right] = int(character)
    return criterion(logits, targets)


def decode(logits: torch.Tensor) -> list[str]:
    sequences = logits.argmax(dim=1).cpu().numpy()
    results = []
    for sequence in sequences:
        characters = []
        previous = BLANK
        for value in sequence:
            if value != BLANK and value != previous:
                characters.append(str(value))
            previous = value
        results.append("".join(characters))
    return results


def evaluate(
    model: CaptchaSequenceCnn,
    images: torch.Tensor,
    labels: list[str],
) -> int:
    model.eval()
    with torch.no_grad():
        predictions = decode(model(images))
    return sum(actual == expected for actual, expected in zip(predictions, labels))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--epochs", type=int, default=12)
    parser.add_argument("--steps-per-epoch", type=int, default=60)
    parser.add_argument("--batch-size", type=int, default=20)
    parser.add_argument("--real-fine-tune-steps", type=int, default=300)
    parser.add_argument("--online-sample-dir", type=Path, required=True)
    parser.add_argument("--resume", type=Path)
    parser.add_argument("--learning-rate", type=float, default=0.001)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    torch.set_num_threads(8)
    torch.manual_seed(SEED)
    np.random.seed(SEED)
    font = primary.load_digit_font()
    model = CaptchaSequenceCnn()
    if args.resume is not None:
        model.load_state_dict(torch.load(args.resume, weights_only=True))
    optimizer = torch.optim.AdamW(
        model.parameters(),
        lr=args.learning_rate,
        weight_decay=1e-5,
    )
    class_weights = torch.ones(11)
    class_weights[BLANK] = 0.15
    criterion = nn.CrossEntropyLoss(weight=class_weights)
    rng = np.random.default_rng(SEED)
    validation_rng = np.random.default_rng(SEED + 1000)
    validation_images, validation_labels, _ = make_synthetic_batch(
        font,
        validation_rng,
        500,
    )
    best_synthetic_correct = -1
    best_synthetic_state = None

    for epoch in range(args.epochs):
        model.train()
        total_loss = 0.0
        for _ in range(args.steps_per_epoch):
            images, labels, starts = make_synthetic_batch(font, rng, args.batch_size)
            loss = calculate_loss(model, images, labels, starts, criterion)
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            optimizer.step()
            total_loss += float(loss.detach())
        correct = evaluate(model, validation_images, validation_labels)
        if correct > best_synthetic_correct:
            best_synthetic_correct = correct
            best_synthetic_state = copy.deepcopy(model.state_dict())
        print(
            f"epoch={epoch + 1} loss={total_loss / args.steps_per_epoch:.4f} "
            f"accuracy={correct}/500",
            flush=True,
        )

    if best_synthetic_state is not None:
        model.load_state_dict(best_synthetic_state)

    fixed_masks = primary.load_real_masks()[:, 0].numpy()
    fixed_labels = list(primary.LABELS)
    online_masks, online_labels = primary.load_online_samples(args.online_sample_dir)
    primary_model = read_primary_model(ARTIFACT_DIR / "captcha_primary_cnn.bin")
    fixed_starts = primary.choose_real_alignments(
        primary_model,
        fixed_masks,
        fixed_labels,
    )
    online_starts = primary.choose_real_alignments(
        primary_model,
        online_masks,
        online_labels,
    )
    holdout_indices = np.arange(4, len(online_labels), 5)
    training_indices = np.setdiff1d(np.arange(len(online_labels)), holdout_indices)
    training_masks = np.concatenate((fixed_masks, online_masks[training_indices]))
    training_labels = [
        *fixed_labels,
        *(online_labels[index] for index in training_indices),
    ]
    training_starts = np.asarray(
        [*fixed_starts, *(online_starts[index] for index in training_indices)]
    )
    holdout_images = build_channels(online_masks[holdout_indices])
    holdout_labels = [online_labels[index] for index in holdout_indices]
    fixed_images = build_channels(fixed_masks)
    real_rng = np.random.default_rng(SEED + 2000)
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.0001, weight_decay=1e-5)
    best_real_score = None
    best_real_state = None
    for step in range(args.real_fine_tune_steps):
        if step % 3:
            indices = real_rng.integers(0, len(training_labels), args.batch_size)
            images = build_channels(training_masks[indices])
            labels = [training_labels[index] for index in indices]
            starts = training_starts[indices]
        else:
            images, labels, starts = make_synthetic_batch(
                font,
                real_rng,
                args.batch_size,
            )
        model.train()
        loss = calculate_loss(model, images, labels, starts, criterion)
        optimizer.zero_grad(set_to_none=True)
        loss.backward()
        optimizer.step()
        if (step + 1) % 100 == 0:
            holdout_correct = evaluate(model, holdout_images, holdout_labels)
            fixed_correct = evaluate(model, fixed_images, fixed_labels)
            checkpoint_score = (holdout_correct + fixed_correct, holdout_correct)
            if best_real_score is None or checkpoint_score > best_real_score:
                best_real_score = checkpoint_score
                best_real_state = copy.deepcopy(model.state_dict())
            print(
                f"real_step={step + 1} loss={float(loss.detach()):.4f} "
                f"holdout={holdout_correct}/{len(holdout_labels)} "
                f"fixed={fixed_correct}/{len(fixed_labels)}",
                flush=True,
            )

    if best_real_state is not None:
        model.load_state_dict(best_real_state)

    ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
    weights_path = ARTIFACT_DIR / "captcha_sequence_crnn.pt"
    torch.save(model.state_dict(), weights_path)
    metadata = {
        "architecture": "full-image convolutional frame-aligned sequence recognizer",
        "input": "raw and thin-line-opened primary masks, 2x80x240 float32",
        "output": "60 frame-supervised time steps over digits 0..9 plus blank",
        "synthetic_training_seed": SEED,
        "epochs": args.epochs,
        "steps_per_epoch": args.steps_per_epoch,
        "batch_size": args.batch_size,
        "real_fine_tune_steps": args.real_fine_tune_steps,
        "real_training_samples": len(training_labels),
        "real_holdout_samples": len(holdout_labels),
        "best_synthetic_accuracy": f"{best_synthetic_correct}/500",
    }
    (ARTIFACT_DIR / "captcha_sequence_crnn.json").write_text(
        json.dumps(metadata, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"weights={weights_path}")


if __name__ == "__main__":
    main()
