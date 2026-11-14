# Motiff Lab sheet music recognition and transcription: homr, copisteria and Transkun behind a small HTTP service,
# for the player's own computer or hosted (0014).
#   docker build -t motiff-omr services/omr
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

FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

# OpenCV (used by homr) needs these shared libraries even in its headless build; ffmpeg takes the sound
# out of a recording for Transkun.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libglib2.0-0 libgl1 ffmpeg \
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
