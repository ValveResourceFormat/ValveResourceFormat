// Calls a publish route of update.s2v.app with this job's OIDC token, which the worker verifies
// to know which workflow and run the request comes from.

const ORIGIN = 'https://update.s2v.app';

// Publishing a dev build copies the viewer artifact before the worker answers, which can take a few minutes
const TIMEOUT = 10 * 60 * 1000;

module.exports = async ({ core }, path, body = {}) => {
    const token = await core.getIDToken(ORIGIN);
    const response = await fetch(`${ORIGIN}${path}`, {
        method: 'POST',
        headers: {
            Authorization: `Bearer ${token}`,
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(TIMEOUT),
    });
    const answer = await response.text();

    if (!response.ok) {
        core.setFailed(`${path} answered ${response.status}: ${answer}`);
        return;
    }

    core.info(answer);
};
