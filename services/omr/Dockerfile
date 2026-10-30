# Motiff Lab sheet music recognition and transcription: homr and Transkun behind a small HTTP service,
# for the player's own computer or hosted (0014).
#   docker build -t motiff-omr services/omr
#   docker run --rm -p 8765:8000 motiff-omr
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
# PyTorch's CPU build, without the gigabytes of CUDA libraries the default one brings.
RUN pip install --index-url https://download.pytorch.org/whl/cpu torch==2.11.0 torchaudio==2.11.0 \
    && pip install -r requirements.txt \
    && pip install --no-deps transkun==2.0.1

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
