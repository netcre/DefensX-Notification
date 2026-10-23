from __future__ import annotations

import argparse
import base64
import ctypes
import hashlib
import json
import math
import re
import struct
import urllib.request
from ctypes import wintypes
from pathlib import Path

import numpy as np
import torch
from PIL import Image
from torch import nn


WIDTH = 240
HEIGHT = 80
LENGTH = 6
CATEGORY_COUNT = 11
BLANK_CATEGORY = 10
OUTPUT_COUNT = 12
ALIGNMENT_INDEX = 11
ALIGNMENT_WEIGHT = 1.0
CROP_LEFT_MARGIN = 12
CROP_WIDTH = 44
SEED = 20260815
UPSTREAM_COMMIT = "74e19a0404aa30abaf93a9dcc4dfce96e9b53a8d"
FONT_SOURCE = (
    "https://cdn.jsdelivr.net/gh/mojocn/base64Captcha@"
    f"{UPSTREAM_COMMIT}/fonts.go"
)
ROOT = Path(__file__).resolve().parents[2]
FIXTURE_DIR = ROOT / "tests" / "DwhMonitor.Tests" / "Fixtures" / "Captcha"
ARTIFACT_DIR = ROOT / "artifacts"

LABELS = (
    "856779 752522 983095 930762 800095 449369 189292 693493 605687 959365 "
    "187456 372502 646671 816974 205018 068330 989121 677186 874800 787350 "
    "290744 213314 978997 059773 342629 324204 842425 937532 212426 298005 "
    "980450 854243 366325 534334 792036 133716 605343 627508 213402 562238 "
    "174572 920847 060470 322849 342814 110724 698236 041803 048237 654652"
).split()


def load_digit_font() -> np.ndarray:
    with urllib.request.urlopen(FONT_SOURCE, timeout=30) as response:
        source = response.read().decode("utf-8")
    body = source.split("var digitFontData = [][]byte{", 1)[1]
    body = body.split("\n}\n", 1)[0]
    body = re.sub(r"//[^\n]*", "", body)
    values = [int(value) for value in re.findall(r"\b[01]\b", body)]
    return np.asarray(values, dtype=np.uint8).reshape(10, 18, 11)


def set_pixel(image: np.ndarray, x: int, y: int) -> None:
    if 0 <= x < WIDTH and 0 <= y < HEIGHT:
        image[y, x] = 1


def draw_horizontal(image: np.ndarray, from_x: int, to_x: int, y: int) -> None:
    if not 0 <= y < HEIGHT:
        return
    left = max(0, from_x)
    right = min(WIDTH - 1, to_x)
    if left <= right:
        image[y, left : right + 1] = 1


def draw_circle(image: np.ndarray, x: int, y: int, radius: int) -> None:
    if radius <= 0:
        set_pixel(image, x, y)
        return
    f = 1 - radius
    delta_x = 1
    delta_y = -2 * radius
    offset_x = 0
    offset_y = radius
    set_pixel(image, x, y + radius)
    set_pixel(image, x, y - radius)
    draw_horizontal(image, x - radius, x + radius, y)
    while offset_x < offset_y:
        if f >= 0:
            offset_y -= 1
            delta_y += 2
            f += delta_y
        offset_x += 1
        delta_x += 2
        f += delta_x
        draw_horizontal(image, x - offset_x, x + offset_x, y + offset_y)
        draw_horizontal(image, x - offset_x, x + offset_x, y - offset_y)
        draw_horizontal(image, x - offset_y, x + offset_y, y + offset_x)
        draw_horizontal(image, x - offset_y, x + offset_y, y - offset_x)


def draw_digit(
    image: np.ndarray,
    glyph: np.ndarray,
    x: int,
    y: int,
    skew: float,
) -> None:
    current_x = float(x)
    for row in range(18):
        for column in range(11):
            if glyph[row, column]:
                draw_circle(image, int(current_x) + column * 2, y + row * 2, 1)
        current_x += skew


def draw_strike(image: np.ndarray, rng: np.random.Generator) -> None:
    line_y = int(rng.integers(HEIGHT // 3, HEIGHT - HEIGHT // 3))
    amplitude = float(rng.uniform(5, 20))
    period = float(rng.uniform(80, 180))
    delta = 2 * math.pi / period
    for x in range(WIDTH):
        offset_x = amplitude * math.cos(line_y * delta)
        offset_y = amplitude * math.sin(x * delta)
        for line_index in range(2):
            radius = int(rng.integers(0, 2)) // 2
            draw_circle(
                image,
                x + int(offset_x),
                line_y + int(offset_y) + line_index * 2,
                radius,
            )


def distort(image: np.ndarray, rng: np.random.Generator) -> np.ndarray:
    amplitude = float(rng.uniform(5, 10))
    period = float(rng.uniform(100, 200))
    delta = 2 * math.pi / period
    y, x = np.indices(image.shape)
    source_x = x + (amplitude * np.sin(y * delta)).astype(np.int32)
    source_y = y + (amplitude * np.cos(x * delta)).astype(np.int32)
    valid = (
        (source_x >= 0)
        & (source_x < WIDTH)
        & (source_y >= 0)
        & (source_y < HEIGHT)
    )
    result = np.zeros_like(image)
    result[valid] = image[source_y[valid], source_x[valid]]
    return result


def add_primary_noise(image: np.ndarray, rng: np.random.Generator) -> None:
    for _ in range(80):
        if int(rng.integers(1, 79)) == 1:
            draw_circle(
                image,
                int(rng.integers(1, WIDTH - 1)),
                int(rng.integers(1, HEIGHT - 1)),
                1,
            )


def remove_thin_lines(image: np.ndarray) -> np.ndarray:
    source = image.astype(bool, copy=False)
    eroded = np.zeros_like(source)
    eroded[1:-1, 1:-1] = (
        source[1:-1, 1:-1]
        & source[:-2, 1:-1]
        & source[2:, 1:-1]
        & source[1:-1, :-2]
        & source[1:-1, 2:]
    )
    opened = eroded.copy()
    opened[:-1] |= eroded[1:]
    opened[1:] |= eroded[:-1]
    opened[:, :-1] |= eroded[:, 1:]
    opened[:, 1:] |= eroded[:, :-1]
    return opened.astype(np.float32)


def render(
    code: str,
    font: np.ndarray,
    rng: np.random.Generator,
    with_interference: bool,
) -> tuple[np.ndarray, int]:
    image = np.zeros((HEIGHT, WIDTH), dtype=np.uint8)
    start_x = int(rng.integers(16, 66))
    x = start_x
    y = int(rng.integers(16, 20))
    for character in code:
        jitter = int(rng.integers(-1, 1))
        skew = float(rng.uniform(-0.7, 0.7))
        draw_digit(image, font[int(character)], x, y + jitter, skew)
        x += 26
    if not with_interference:
        return image, start_x
    draw_strike(image, rng)
    image = distort(image, rng)
    add_primary_noise(image, rng)
    return image, start_x


def primary_mask(image: Image.Image) -> np.ndarray:
    """Build the same dominant-color mask used by the C# release recognizer."""
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8)
    visible = rgba[:, :, 3] != 0
    non_white = ~(
        (rgba[:, :, 0] > 245)
        & (rgba[:, :, 1] > 245)
        & (rgba[:, :, 2] > 245)
    )
    eligible = visible & non_white
    if not np.any(eligible):
        return np.zeros((HEIGHT, WIDTH), dtype=np.float32)
    colors, counts = np.unique(rgba[eligible], axis=0, return_counts=True)
    primary = colors[int(np.argmax(counts))]
    return np.all(rgba == primary, axis=2).astype(np.float32)


def load_real_masks() -> torch.Tensor:
    masks = []
    for index in range(1, len(LABELS) + 1):
        image = Image.open(FIXTURE_DIR / f"sample-{index:02d}.png")
        masks.append(primary_mask(image))
    return torch.from_numpy(np.asarray(masks)[:, None])


def crop_window(image: np.ndarray, left: int) -> np.ndarray:
    result = np.zeros((HEIGHT, CROP_WIDTH), dtype=np.float32)
    source_left = max(0, left)
    source_right = min(WIDTH, left + CROP_WIDTH)
    if source_left < source_right:
        result[:, source_left - left : source_right - left] = image[
            :, source_left:source_right
        ]
    return result


class CaptchaCnn(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv2d(2, 16, 3, padding=1),
            nn.ReLU(),
            nn.MaxPool2d((2, 2)),
            nn.Conv2d(16, 32, 3, padding=1),
            nn.ReLU(),
            nn.MaxPool2d((2, 2)),
            nn.Conv2d(32, 64, 3, padding=1),
            nn.ReLU(),
            nn.AdaptiveMaxPool2d((4, 4)),
        )
        self.classifier = nn.Sequential(
            nn.Flatten(),
            nn.Linear(64 * 4 * 4, 128),
            nn.ReLU(),
            nn.Linear(128, OUTPUT_COUNT),
        )

    def forward(self, images: torch.Tensor) -> torch.Tensor:
        return self.classifier(self.features(images))


def make_batch(
    font: np.ndarray,
    rng: np.random.Generator,
    batch_size: int,
    with_interference: bool,
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    images = np.empty(
        (batch_size * (LENGTH * 2 + 2), 2, HEIGHT, CROP_WIDTH),
        dtype=np.float32,
    )
    targets = np.empty(batch_size * (LENGTH * 2 + 2), dtype=np.int64)
    alignment_targets = np.empty(batch_size * (LENGTH * 2 + 2), dtype=np.float32)
    for captcha_index in range(batch_size):
        digits = rng.integers(0, 10, LENGTH)
        code = "".join(str(value) for value in digits)
        captcha, start_x = render(code, font, rng, with_interference)
        opened = remove_thin_lines(captcha)
        for position, digit in enumerate(digits):
            target_index = captcha_index * (LENGTH * 2 + 2) + position
            jitter = int(rng.integers(-2, 3))
            left = start_x + position * 26 - CROP_LEFT_MARGIN + jitter
            images[target_index, 0] = crop_window(captcha, left)
            images[target_index, 1] = crop_window(opened, left)
            targets[target_index] = digit
            alignment_targets[target_index] = 1
            misaligned_index = target_index + LENGTH
            offset = int(rng.integers(7, 15))
            if int(rng.integers(0, 2)) == 0:
                offset = -offset
            misaligned_left = start_x + position * 26 - CROP_LEFT_MARGIN + offset
            images[misaligned_index, 0] = crop_window(captcha, misaligned_left)
            images[misaligned_index, 1] = crop_window(opened, misaligned_left)
            targets[misaligned_index] = digit
            alignment_targets[misaligned_index] = 0
        for blank_index, blank_position in enumerate((-1, LENGTH)):
            target_index = (
                captcha_index * (LENGTH * 2 + 2) + LENGTH * 2 + blank_index
            )
            left = (
                start_x
                + blank_position * 26
                - CROP_LEFT_MARGIN
                + int(rng.integers(-3, 4))
            )
            images[target_index, 0] = crop_window(captcha, left)
            images[target_index, 1] = crop_window(opened, left)
            targets[target_index] = BLANK_CATEGORY
            alignment_targets[target_index] = 0
    return (
        torch.from_numpy(images),
        torch.from_numpy(targets),
        torch.from_numpy(alignment_targets),
    )


def decode(logits: torch.Tensor) -> list[str]:
    return [str(value) for value in logits.argmax(dim=1).cpu().numpy()]


def recognize_captchas(
    model: CaptchaCnn,
    masks: torch.Tensor,
) -> tuple[list[str], list[float]]:
    predictions = []
    confidences = []
    for mask in masks[:, 0].numpy():
        crops = np.empty((50 * LENGTH, 2, HEIGHT, CROP_WIDTH), dtype=np.float32)
        opened = remove_thin_lines(mask)
        for candidate_index, start_x in enumerate(range(16, 66)):
            for position in range(LENGTH):
                left = start_x + position * 26 - CROP_LEFT_MARGIN
                crops[candidate_index * LENGTH + position, 0] = crop_window(mask, left)
                crops[candidate_index * LENGTH + position, 1] = crop_window(opened, left)
        with torch.no_grad():
            logits = model(torch.from_numpy(crops))
            probabilities = logits[:, :CATEGORY_COUNT].softmax(dim=1)
        alignment = logits[:, ALIGNMENT_INDEX].sigmoid()
        values, digits = probabilities[:, :10].max(dim=1)
        values = values.reshape(50, LENGTH)
        digits = digits.reshape(50, LENGTH)
        alignment = alignment.reshape(50, LENGTH)
        joint_scores = (
            values.clamp_min(1e-8).log().sum(dim=1)
            + ALIGNMENT_WEIGHT * alignment.clamp_min(1e-8).log().sum(dim=1)
        )
        winner = int(joint_scores.argmax())
        predictions.append(
            "".join(str(value) for value in digits[winner].cpu().numpy())
        )
        confidences.append(float(values[winner].mean()))
    return predictions, confidences


def choose_real_alignments(
    model: CaptchaCnn,
    masks: np.ndarray,
    labels: list[str],
) -> list[int]:
    alignments = []
    for mask, label in zip(masks, labels):
        opened = remove_thin_lines(mask)
        crops = np.empty((50 * LENGTH, 2, HEIGHT, CROP_WIDTH), dtype=np.float32)
        for candidate_index, start_x in enumerate(range(16, 66)):
            for position in range(LENGTH):
                left = start_x + position * 26 - CROP_LEFT_MARGIN
                target_index = candidate_index * LENGTH + position
                crops[target_index, 0] = crop_window(mask, left)
                crops[target_index, 1] = crop_window(opened, left)
        with torch.no_grad():
            logits = model(torch.from_numpy(crops))
            probabilities = logits[:, :CATEGORY_COUNT].softmax(dim=1)
        probabilities = probabilities.reshape(50, LENGTH, CATEGORY_COUNT)
        targets = torch.tensor([int(character) for character in label])
        scores = torch.stack(
            [
                probabilities[index, torch.arange(LENGTH), targets]
                .clamp_min(1e-9)
                .log()
                .sum()
                for index in range(50)
            ]
        )
        alignments.append(16 + int(scores.argmax()))
    return alignments


def make_real_batch(
    masks: np.ndarray,
    labels: list[str],
    alignments: list[int],
    rng: np.random.Generator,
    batch_size: int,
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    images = np.empty(
        (batch_size * (LENGTH * 2 + 2), 2, HEIGHT, CROP_WIDTH),
        dtype=np.float32,
    )
    targets = np.empty(batch_size * (LENGTH * 2 + 2), dtype=np.int64)
    alignment_targets = np.empty(batch_size * (LENGTH * 2 + 2), dtype=np.float32)
    for batch_index, fixture_index in enumerate(rng.integers(0, len(labels), batch_size)):
        mask = masks[fixture_index]
        opened = remove_thin_lines(mask)
        start_x = alignments[fixture_index]
        for position, character in enumerate(labels[fixture_index]):
            target_index = batch_index * (LENGTH * 2 + 2) + position
            left = (
                start_x
                + position * 26
                - CROP_LEFT_MARGIN
                + int(rng.integers(-2, 3))
            )
            images[target_index, 0] = crop_window(mask, left)
            images[target_index, 1] = crop_window(opened, left)
            targets[target_index] = int(character)
            alignment_targets[target_index] = 1
            misaligned_index = target_index + LENGTH
            offset = int(rng.integers(7, 15))
            if int(rng.integers(0, 2)) == 0:
                offset = -offset
            misaligned_left = (
                start_x + position * 26 - CROP_LEFT_MARGIN + offset
            )
            images[misaligned_index, 0] = crop_window(mask, misaligned_left)
            images[misaligned_index, 1] = crop_window(opened, misaligned_left)
            targets[misaligned_index] = int(character)
            alignment_targets[misaligned_index] = 0
        for blank_index, blank_position in enumerate((-1, LENGTH)):
            target_index = batch_index * (LENGTH * 2 + 2) + LENGTH * 2 + blank_index
            left = (
                start_x
                + blank_position * 26
                - CROP_LEFT_MARGIN
                + int(rng.integers(-3, 4))
            )
            images[target_index, 0] = crop_window(mask, left)
            images[target_index, 1] = crop_window(opened, left)
            targets[target_index] = BLANK_CATEGORY
            alignment_targets[target_index] = 0
    return (
        torch.from_numpy(images),
        torch.from_numpy(targets),
        torch.from_numpy(alignment_targets),
    )


def calculate_loss(
    logits: torch.Tensor,
    targets: torch.Tensor,
    alignment_targets: torch.Tensor,
    criterion: nn.CrossEntropyLoss,
) -> torch.Tensor:
    classification_loss = criterion(logits[:, :CATEGORY_COUNT], targets)
    alignment_loss = nn.functional.binary_cross_entropy_with_logits(
        logits[:, ALIGNMENT_INDEX],
        alignment_targets,
    )
    return classification_loss + 0.3 * alignment_loss


class DataBlob(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_byte))]


def unprotect_current_user(protected: bytes) -> str:
    source_buffer = ctypes.create_string_buffer(protected)
    source = DataBlob(
        len(protected),
        ctypes.cast(source_buffer, ctypes.POINTER(ctypes.c_byte)),
    )
    destination = DataBlob()
    if not ctypes.windll.crypt32.CryptUnprotectData(
        ctypes.byref(source),
        None,
        None,
        None,
        None,
        0,
        ctypes.byref(destination),
    ):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(destination.data, destination.size).decode("utf-8")
    finally:
        ctypes.windll.kernel32.LocalFree(destination.data)


def load_online_samples(directory: Path) -> tuple[np.ndarray, list[str]]:
    masks = []
    labels = []
    for label_path in sorted(directory.glob("*.label.dpapi")):
        image_path = label_path.with_name(label_path.name.removesuffix(".label.dpapi") + ".png")
        if not image_path.exists():
            continue
        label = unprotect_current_user(base64.b64decode(label_path.read_text(encoding="utf-8")))
        if len(label) != LENGTH or not label.isdigit():
            raise ValueError(f"Invalid protected label for {image_path.name}")
        image = Image.open(image_path)
        masks.append(primary_mask(image))
        labels.append(label)
    return np.asarray(masks, dtype=np.float32), labels


def write_model(model: CaptchaCnn, path: Path) -> str:
    state = model.state_dict()
    with path.open("wb") as stream:
        stream.write(b"DWCMODEL1")
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
    parser.add_argument("--epochs", type=int, default=30)
    parser.add_argument("--steps-per-epoch", type=int, default=100)
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--real-fine-tune-steps", type=int, default=600)
    parser.add_argument("--online-sample-dir", type=Path)
    parser.add_argument("--online-fine-tune-steps", type=int, default=600)
    parser.add_argument("--online-learning-rate", type=float, default=0.00003)
    parser.add_argument("--hard-sample-dir", type=Path)
    parser.add_argument("--hard-fine-tune-steps", type=int, default=80)
    parser.add_argument("--hard-learning-rate", type=float, default=0.0002)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    torch.set_num_threads(8)
    torch.manual_seed(SEED)
    np.random.seed(SEED)
    font = load_digit_font()
    training_rng = np.random.default_rng(SEED)
    model = CaptchaCnn()
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.001, weight_decay=1e-5)
    criterion = nn.CrossEntropyLoss()

    for epoch in range(args.epochs):
        model.train()
        total_loss = 0.0
        with_interference = epoch >= 3
        for _ in range(args.steps_per_epoch):
            images, targets, alignment_targets = make_batch(
                font,
                training_rng,
                args.batch_size,
                with_interference,
            )
            logits = model(images)
            loss = calculate_loss(logits, targets, alignment_targets, criterion)
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            optimizer.step()
            total_loss += float(loss.detach())

        model.eval()
        validation_rng = np.random.default_rng(SEED + 1000 + epoch)
        validation_images, validation_targets, validation_alignment = make_batch(
            font, validation_rng, 50, True
        )
        with torch.no_grad():
            validation_logits = model(validation_images)
            validation_correct = int(
                (
                    validation_logits[:, :CATEGORY_COUNT].argmax(dim=1)
                    == validation_targets
                ).sum()
            )
            validation_alignment_correct = int(
                (
                    (validation_logits[:, ALIGNMENT_INDEX] >= 0)
                    == (validation_alignment >= 0.5)
                ).sum()
            )
        print(
            f"epoch={epoch + 1} loss={total_loss / args.steps_per_epoch:.4f} "
            f"synthetic_windows={validation_correct}/{50 * (LENGTH * 2 + 2)} "
            f"alignment={validation_alignment_correct}/{50 * (LENGTH * 2 + 2)}",
            flush=True,
        )

    real_masks_tensor = load_real_masks()
    real_masks = real_masks_tensor[:, 0].numpy()
    model.eval()
    fixed_labels = list(LABELS)
    alignments = choose_real_alignments(model, real_masks, fixed_labels)
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.0002, weight_decay=1e-5)
    real_rng = np.random.default_rng(SEED + 2000)
    model.train()
    for step in range(args.real_fine_tune_steps):
        images, targets, alignment_targets = make_real_batch(
            real_masks,
            fixed_labels,
            alignments,
            real_rng,
            args.batch_size,
        )
        logits = model(images)
        loss = calculate_loss(logits, targets, alignment_targets, criterion)
        optimizer.zero_grad(set_to_none=True)
        loss.backward()
        optimizer.step()
        if (step + 1) % 100 == 0:
            print(
                f"real_fine_tune_step={step + 1} loss={float(loss.detach()):.4f}",
                flush=True,
            )

    online_sample_count = 0
    if args.online_sample_dir is not None:
        online_masks, online_labels = load_online_samples(args.online_sample_dir)
        online_sample_count = len(online_labels)
        if online_sample_count == 0:
            raise ValueError("No accepted online samples were found")

    hard_sample_count = 0
    base_weights_sha256 = None
    selected_hard_step = 0
    if args.hard_sample_dir is not None:
        if online_sample_count == 0:
            raise ValueError("Hard-sample training requires accepted online samples")
        hard_masks, hard_labels = load_online_samples(args.hard_sample_dir)
        hard_sample_count = len(hard_labels)
        if hard_sample_count == 0:
            raise ValueError("No protected hard samples were found")

        model.eval()
        online_alignments = choose_real_alignments(model, online_masks, online_labels)
        hard_alignments = choose_real_alignments(model, hard_masks, hard_labels)
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        base_weights_sha256 = write_model(
            model,
            ARTIFACT_DIR / "captcha_primary_base.bin",
        )
        for name, parameter in model.named_parameters():
            parameter.requires_grad_(name.startswith("classifier.3."))
        optimizer = torch.optim.AdamW(
            (parameter for parameter in model.parameters() if parameter.requires_grad),
            lr=args.hard_learning_rate,
            weight_decay=1e-5,
        )
        hard_rng = np.random.default_rng(SEED + 4000)
        best_state = None
        best_score = None
        model.train()
        for step in range(args.hard_fine_tune_steps):
            phase = step % 6
            if phase in (0, 1):
                images, targets, alignment_targets = make_real_batch(
                    hard_masks,
                    hard_labels,
                    hard_alignments,
                    hard_rng,
                    args.batch_size,
                )
            elif phase == 2:
                images, targets, alignment_targets = make_real_batch(
                    online_masks,
                    online_labels,
                    online_alignments,
                    hard_rng,
                    args.batch_size,
                )
            elif phase in (3, 4):
                images, targets, alignment_targets = make_real_batch(
                    real_masks,
                    fixed_labels,
                    alignments,
                    hard_rng,
                    args.batch_size,
                )
            else:
                images, targets, alignment_targets = make_batch(
                    font,
                    hard_rng,
                    args.batch_size,
                    with_interference=True,
                )
            logits = model(images)
            loss = calculate_loss(logits, targets, alignment_targets, criterion)
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            optimizer.step()
            if (step + 1) % 30 == 0:
                model.eval()
                fixed_predictions, _ = recognize_captchas(model, real_masks_tensor)
                fixed_checkpoint_correct = sum(
                    actual == expected
                    for actual, expected in zip(fixed_predictions, fixed_labels)
                )
                hard_predictions, _ = recognize_captchas(
                    model,
                    torch.from_numpy(hard_masks[:, None]),
                )
                hard_checkpoint_correct = sum(
                    actual == expected
                    for actual, expected in zip(hard_predictions, hard_labels)
                )
                print(
                    f"hard_fine_tune_step={step + 1} "
                    f"loss={float(loss.detach()):.4f} "
                    f"fixed={fixed_checkpoint_correct}/50 "
                    f"hard={hard_checkpoint_correct}/{hard_sample_count}",
                    flush=True,
                )
                checkpoint_score = (
                    hard_checkpoint_correct + fixed_checkpoint_correct,
                    hard_checkpoint_correct,
                    fixed_checkpoint_correct,
                )
                if (
                    fixed_checkpoint_correct >= 48
                    and (best_score is None or checkpoint_score > best_score)
                ):
                    best_score = checkpoint_score
                    selected_hard_step = step + 1
                    best_state = {
                        name: value.detach().clone()
                        for name, value in model.state_dict().items()
                    }
                model.train()
        if best_state is None:
            raise ValueError("Hard-sample training produced no checkpoint at 48/50")
        model.load_state_dict(best_state)
    elif online_sample_count:
        model.eval()
        online_alignments = choose_real_alignments(model, online_masks, online_labels)
        optimizer = torch.optim.AdamW(
            model.parameters(),
            lr=args.online_learning_rate,
            weight_decay=1e-5,
        )
        online_rng = np.random.default_rng(SEED + 3000)
        model.train()
        for step in range(args.online_fine_tune_steps):
            phase = step % 3
            if phase == 0:
                images, targets, alignment_targets = make_real_batch(
                    online_masks,
                    online_labels,
                    online_alignments,
                    online_rng,
                    args.batch_size,
                )
            elif phase == 1:
                images, targets, alignment_targets = make_real_batch(
                    real_masks,
                    fixed_labels,
                    alignments,
                    online_rng,
                    args.batch_size,
                )
            else:
                images, targets, alignment_targets = make_batch(
                    font,
                    online_rng,
                    args.batch_size,
                    with_interference=True,
                )
            logits = model(images)
            loss = calculate_loss(logits, targets, alignment_targets, criterion)
            optimizer.zero_grad(set_to_none=True)
            loss.backward()
            optimizer.step()
            if (step + 1) % 100 == 0:
                print(
                    f"online_fine_tune_step={step + 1} "
                    f"loss={float(loss.detach()):.4f}",
                    flush=True,
                )

    model.eval()
    predictions, confidences = recognize_captchas(model, real_masks_tensor)
    fixed_correct = sum(
        actual == expected for actual, expected in zip(predictions, LABELS)
    )
    print(f"real_fixed_accuracy={fixed_correct}/50")
    for index, (expected, actual) in enumerate(zip(LABELS, predictions), 1):
        if expected != actual:
            print(
                f"miss={index:02d} expected={expected} actual={actual} "
                f"confidence={confidences[index - 1]:.4f}"
            )

    ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
    model_path = ARTIFACT_DIR / "captcha_primary_cnn.bin"
    weights_sha256 = write_model(model, model_path)
    metadata = {
        "format": "DWCMODEL1",
        "architecture": (
            "Conv16-MaxPool2-Conv32-MaxPool2-Conv64-"
            "AdaptiveMaxPool4x4-FC128-FC12 shared-character-alignment-scan"
        ),
        "input": (
            "raw and thin-line-opened dominant RGBA-color masks, "
            f"50x6 crops of 2x{CROP_WIDTH}x{HEIGHT} float32"
        ),
        "charset": "0123456789 plus blank category and alignment logit",
        "weights_sha256": weights_sha256,
        "training": (
            "upstream-equivalent synthetic DriverDigit generator, then "
            "50 labeled real fixtures with crop translation augmentation"
        ),
        "upstream_commit": UPSTREAM_COMMIT,
        "synthetic_training_seed": SEED,
        "epochs": args.epochs,
        "steps_per_epoch": args.steps_per_epoch,
        "batch_size": args.batch_size,
        "real_fine_tune_steps": args.real_fine_tune_steps,
        "online_fine_tune_steps": (
            args.online_fine_tune_steps
            if online_sample_count and hard_sample_count == 0
            else 0
        ),
        "online_learning_rate": args.online_learning_rate,
        "online_replay": "online:fixed:synthetic=1:1:1",
        "online_training_samples": online_sample_count,
        "hard_training_samples": hard_sample_count,
        "hard_fine_tune_steps": selected_hard_step,
        "hard_learning_rate": args.hard_learning_rate,
        "hard_replay": "hard:accepted:fixed:synthetic=2:1:2:1",
        "base_weights_sha256": base_weights_sha256,
        "real_fixed_accuracy": f"{fixed_correct}/50",
    }
    metadata_path = ARTIFACT_DIR / "captcha_primary_cnn.json"
    metadata_path.write_text(
        json.dumps(metadata, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"model_sha256={weights_sha256}")


if __name__ == "__main__":
    main()
