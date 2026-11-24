# Motiff Lab sheet music recognition and transcription: homr, copisteria, Audiveris, Transcoda and Transkun behind a
# small HTTP service, for the player's own computer or hosted (0014, 0035).
#   docker build -t motiff-omr src/backend/omr
#   docker run --rm -p 8765:8000 motiff-omr

# copisteria (KobiMusic, Apache-2.0), the second engine for scans, at a pinned commit, with copista-28m's weights
# (Apache-2.0) from Hugging Face at a pinned revision, checked by their SHA-256. Fetched in a stage of its own, so git
# and curl stay out of the image. Its LICENSE and NOTICE files come with it, and the weights' NOTICE beside them.
FROM python:3.12-slim AS copisteria
ARG COPISTERIA_COMMIT=c685f280914d91a694494588d9f6a376d7917eba
ARG COPISTA_REVISION=44464d11234a22ba23634ec8eb1e31e3b012021a
RUN apt-get update \
    && apt-get install --yes --no-install-recommends git curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /opt/copisteria
RUN git init --quiet . \
    && git fetch --quiet --depth 1 https://github.com/kobimusic/copisteria "$COPISTERIA_COMMIT" \
    && git checkout --quiet --detach FETCH_HEAD \
    && rm -rf .git tests docs
RUN weights="https://huggingface.co/kobimusic/copista-28m/resolve/$COPISTA_REVISION" \
    && mkdir -p models/small \
    && curl -fsSL -o models/evidence-2m.pt "$weights/evidence-2m.pt" \
    && curl -fsSL -o models/small/v7_obj-recall-30m_best.pt "$weights/small/v7_obj-recall-30m_best.pt" \
    && curl -fsSL -o models/NOTICE.copista-28m "$weights/NOTICE" \
    && printf '%s  %s\n' \
        7a3eb0362b44016e941680c5df1e133239576592ab134057c3dbac8e9f3b8838 models/evidence-2m.pt \
        9f1db282271fb1064461387da4a2e15de14b099c49b619c21429cdc45be85cfe models/small/v7_obj-recall-30m_best.pt \
        | sha256sum --check --quiet -

# Audiveris 5.11.0 (AGPL-3.0), the third engine for scans: its published Linux package, checked by its SHA-256 and
# unpacked, not installed (it would bring a desktop's menus). It carries its own Java runtime in /opt/audiveris/lib/runtime,
# and its copyright file in /opt/audiveris/share/doc.
FROM python:3.12-slim AS audiveris
ARG AUDIVERIS_VERSION=5.11.0
ARG AUDIVERIS_SHA256=f20113aaa33b3149ec8d6a09b2a7963360e65fafd92d69389987a85bbc3ec7a3
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN curl -fsSL -o /tmp/audiveris.deb \
        "https://github.com/Audiveris/audiveris/releases/download/$AUDIVERIS_VERSION/Audiveris-$AUDIVERIS_VERSION-ubuntu24.04-x86_64.deb" \
    && printf '%s  %s\n' "$AUDIVERIS_SHA256" /tmp/audiveris.deb | sha256sum --check --quiet - \
    && dpkg-deb --extract /tmp/audiveris.deb /tmp/unpacked \
    && mv /tmp/unpacked/opt/audiveris /opt/audiveris \
    && rm -rf /tmp/audiveris.deb /tmp/unpacked

# Transcoda 2.1 (Heinrich Heine University Düsseldorf), the fourth engine for scans: its code (AGPL-3.0) at the release's
# commit, in an environment of its own made from its lock file (its PyTorch, Transformers and NumPy differ from homr's),
# and its weights (CC BY-NC 4.0, non-commercial) from Hugging Face at a pinned revision, checked by their SHA-256, with
# the ConvNeXt V2 encoder they start from, which Transcoda loads before its weights. Only what its inference needs is
# kept: its training, evaluation and data-generation code and the Rust and vendored parsers it builds them with are not.
FROM python:3.12-slim AS transcoda
ARG TRANSCODA_COMMIT=389cca243cb0c531281524662d86da2fe7362703
ARG TRANSCODA_REVISION=6a46a8c2534c882fab8c0b4f9647b78c4e289bde
ARG CONVNEXT_REVISION=9cba4896e97bb86b1eb609e482a2149d84f345bc
RUN apt-get update \
    && apt-get install --yes --no-install-recommends git curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /opt/transcoda
RUN git init --quiet . \
    && git fetch --quiet --depth 1 https://github.com/btrkeks/transcoda "$TRANSCODA_COMMIT" \
    && git checkout --quiet --detach FETCH_HEAD \
    && rm -rf .git tests docs evaluation examples pipelines vendor crates Cargo.toml Cargo.lock train.py
RUN pip install --no-cache-dir uv==0.11.32 \
    && UV_PROJECT_ENVIRONMENT=/opt/transcoda/.venv UV_PYTHON_DOWNLOADS=never \
        uv sync --frozen --no-dev --no-install-project --no-cache --extra cpu --extra constraints --python /usr/local/bin/python3.12
RUN weights="https://huggingface.co/btrkeks/transcoda-v2.1/resolve/$TRANSCODA_REVISION" \
    && encoder="https://huggingface.co/facebook/convnextv2-tiny-22k-224/resolve/$CONVNEXT_REVISION" \
    && cache=hf/hub/models--facebook--convnextv2-tiny-22k-224 \
    && mkdir -p weights "$cache/snapshots/$CONVNEXT_REVISION" "$cache/refs" \
    && curl -fsSL -o weights/transcoda-v2.1.ckpt "$weights/transcoda-v2.1.ckpt" \
    && curl -fsSL -o weights/LICENSE "$weights/LICENSE" \
    && curl -fsSL -o weights/NOTICE.md "$weights/NOTICE.md" \
    && curl -fsSL -o weights/UPSTREAM_LICENSE "$weights/UPSTREAM_LICENSE" \
    && curl -fsSL -o "$cache/snapshots/$CONVNEXT_REVISION/config.json" "$encoder/config.json" \
    && curl -fsSL -o "$cache/snapshots/$CONVNEXT_REVISION/model.safetensors" "$encoder/model.safetensors" \
    && printf '%s' "$CONVNEXT_REVISION" > "$cache/refs/main" \
    && printf '%s  %s\n' \
        7016c0c9bb5896ccf2e64624d566427b9e3a524fa57d8ae649609f5aa562a5bf weights/transcoda-v2.1.ckpt \
        336255dc30193e8e15d689d9481bb05673d89055718f3a96923a7ffb99adbbaf weights/LICENSE \
        4449f11f442ad7af8c2e2a8efce018d541faa8295d16cebf42e08ca18ea9dff4 weights/NOTICE.md \
        9ab7947f327be897e841ce58b90996c4b814c35127897b383f17616510115b9b weights/UPSTREAM_LICENSE \
        f7674180aaa6ff41308f448cc06a252e7f95a959b4f93d2b57c721d473e64069 "$cache/snapshots/$CONVNEXT_REVISION/config.json" \
        b33653bb8c060f6dee6438f18c559dcf3258bf86cc906490daaca89bc0c39fb7 "$cache/snapshots/$CONVNEXT_REVISION/model.safetensors" \
        | sha256sum --check --quiet -

FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

# OpenCV (used by homr) needs these shared libraries even in its headless build; ffmpeg takes the sound
# out of a recording for Transkun; Audiveris's Java runtime the fonts and X libraries its package depends on, and GTK,
# which Audiveris loads at start to find its folders, even in batch mode.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libglib2.0-0 libgl1 ffmpeg \
        libfreetype6 libfontconfig1 libasound2t64 libx11-6 libxext6 libxi6 libxrender1 libxtst6 libgtk-3-0t64 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /srv
COPY requirements.txt .
# PyTorch's CPU build, without the gigabytes of CUDA libraries the default one brings; torchvision for copisteria.
RUN pip install --index-url https://download.pytorch.org/whl/cpu torch==2.11.0 torchaudio==2.11.0 torchvision==0.26.0 \
    && pip install -r requirements.txt \
    && pip install --no-deps transkun==2.0.1

# copisteria is installed editable, without its own requirements (homr has brought NumPy, SciPy, Pillow and OpenCV's
# headless build, which stands in for the opencv-python it names): it finds its models/ folder beside its code.
COPY --from=copisteria /opt/copisteria /opt/copisteria
RUN pip install --no-deps --editable /opt/copisteria

COPY --from=audiveris /opt/audiveris /opt/audiveris
COPY --from=transcoda /opt/transcoda /opt/transcoda

# homr's models (about 190 MB) are downloaded once, into the image, so the service works offline;
# Transkun's (56 MB) come in its package.
RUN homr --gpu no --init

RUN useradd --create-home --uid 10001 scan
COPY app ./app
USER scan

EXPOSE 8000
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8000/health')"
# No access log: request lines name no music, but there is nothing in them the player needs either.
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", "--no-access-log"]
