export async function parseBoardviewFile(file) {
    const formData = new FormData();
    formData.append('file', file, file.name);

    let resp;
    try {
        resp = await fetch('/api/boardview/parse', {
            method: 'POST',
            body: formData
        });
    } catch {
        throw new Error('Failed to connect to server.');
    }

    if (!resp.ok) {
        let errInfo = {};
        try { errInfo = await resp.json(); } catch { }
        throw new Error(errInfo.error || errInfo.message || 'Failed to parse Boardview.');
    }
    return await resp.json();
}
