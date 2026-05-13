const isPasskeySupported =
    typeof navigator.credentials !== 'undefined' &&
    typeof window.PublicKeyCredential !== 'undefined' &&
    typeof window.PublicKeyCredential.parseCreationOptionsFromJSON === 'function' &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === 'function';

let conditionalAbortController = null;

export function isSupported() {
    return isPasskeySupported;
}

export async function isConditionalMediationAvailable() {
    if (!isPasskeySupported) return false;
    return await (PublicKeyCredential.isConditionalMediationAvailable?.() ?? false);
}

async function fetchOptions(optionsEndpointUrl) {
    const response = await fetch(optionsEndpointUrl, {
        method: 'POST',
        credentials: 'include',
    });
    if (!response.ok) throw new Error(`Failed to fetch passkey options: ${response.status}`);
    return await response.text();
}

async function postCredential(actionEndpointUrl, credential) {
    const response = await fetch(actionEndpointUrl, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(credential),
    });
    const text = await response.text();
    if (!response.ok) throw new Error(text || `Request failed: ${response.status}`);
    return text;
}

export async function requestCredential(optionsEndpointUrl, actionEndpointUrl) {
    if (!isPasskeySupported) throw new Error('Passkeys are not supported in this browser.');
    cancelConditionalMediation();
    const optionsJson = await fetchOptions(optionsEndpointUrl);
    const options = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson));
    const credential = await navigator.credentials.get({ publicKey: options });
    return await postCredential(actionEndpointUrl, credential);
}

export async function createCredential(optionsEndpointUrl, actionEndpointUrl) {
    if (!isPasskeySupported) throw new Error('Passkeys are not supported in this browser.');
    const optionsJson = await fetchOptions(optionsEndpointUrl);
    const options = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson));
    const credential = await navigator.credentials.create({ publicKey: options });
    return await postCredential(actionEndpointUrl, credential);
}

export async function startConditionalMediation(optionsEndpointUrl, actionEndpointUrl, dotNetRef) {
    if (!isPasskeySupported) return;
    if (!await isConditionalMediationAvailable()) return;

    cancelConditionalMediation();
    conditionalAbortController = new AbortController();

    try {
        const optionsJson = await fetchOptions(optionsEndpointUrl);
        const options = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson));
        const credential = await navigator.credentials.get({
            publicKey: options,
            mediation: 'conditional',
            signal: conditionalAbortController.signal,
        });
        conditionalAbortController = null;
        const redirectUrl = await postCredential(actionEndpointUrl, credential);
        await dotNetRef.invokeMethodAsync('HandlePasskeyAutofill', redirectUrl);
    } catch (e) {
        if (e.name !== 'AbortError') {
            console.warn('Conditional passkey mediation error:', e);
        }
    }
}

export function cancelConditionalMediation() {
    if (conditionalAbortController) {
        conditionalAbortController.abort();
        conditionalAbortController = null;
    }
}
