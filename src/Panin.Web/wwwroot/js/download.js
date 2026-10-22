// Mengunduh berkas dari aliran .NET (DotNetStreamReference) tanpa endpoint tambahan.
export async function downloadFromStream(fileName, contentType, streamRef) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}
