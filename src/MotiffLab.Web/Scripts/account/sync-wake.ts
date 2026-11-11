// When to sync the library again (version 0.2, ProjectSync S10): the page coming back into view, and the network coming
// back. The library calls back into .NET, which runs one sync pass; nothing here touches a project.

interface DotNetRef {
    invokeMethodAsync(method: string): Promise<unknown>;
}

/** Calls `Wake` on the library when the page is shown again or the network returns; the function given back stops it. */
export function watch(library: DotNetRef): () => void {
    const wake = (): void => {
        if (document.visibilityState === "visible") {
            void library.invokeMethodAsync("Wake").catch(() => undefined);
        }
    };
    document.addEventListener("visibilitychange", wake);
    window.addEventListener("online", wake);
    return () => {
        document.removeEventListener("visibilitychange", wake);
        window.removeEventListener("online", wake);
    };
}

/** Stops what `watch` started. */
export function stop(unwatch: () => void): void {
    unwatch();
}
