// Serves the published app (dotnet publish ... --output publish) for the browser tests, the way
// Static Web Apps does (staticwebapp.config.json): files as they are, and index.html for any other
// path, so the router and the service worker see what players see. No dependencies.
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";

const root = resolve(process.argv[2] ?? "publish/wwwroot");
const port = Number(process.argv[3] ?? 5180);
const types = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript",
    ".mjs": "text/javascript",
    ".css": "text/css",
    ".json": "application/json",
    ".webmanifest": "application/manifest+json",
    ".wasm": "application/wasm",
    ".png": "image/png",
    ".mp3": "audio/mpeg",
    ".txt": "text/plain",
    ".map": "application/json",
};

async function file(path) {
    try {
        return (await stat(path)).isFile() ? path : null;
    } catch {
        return null;
    }
}

createServer(async (request, response) => {
    const path = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
    const local = normalize(join(root, path));
    const found = local.startsWith(root) ? await file(local) : null;
    const served = found ?? (extname(path) === "" ? join(root, "index.html") : null);
    if (served === null) {
        response.writeHead(404).end();
        return;
    }

    response.writeHead(200, { "Content-Type": types[extname(served)] ?? "application/octet-stream", "Cache-Control": "no-cache" });
    response.end(await readFile(served));
}).listen(port, "127.0.0.1", () => console.log(`Serving ${root} on http://localhost:${port}`));
