const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json', ...CORS_HEADERS },
  });
}

export async function onRequestOptions() {
  return new Response(null, { status: 204, headers: CORS_HEADERS });
}

export async function onRequestPost(context) {
  const { request, env } = context;

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ ok: false, error: 'invalid_json' }, 400);
  }

  // Silent honeypot trap for spam bots
  if (body.hp && String(body.hp).trim().length > 0) {
    return json({ ok: true }, 200);
  }

  const message = String(body.message || '').trim();
  if (message.length < 5) {
    return json({ ok: false, error: 'too_short' }, 400);
  }
  if (message.length > 2000) {
    return json({ ok: false, error: 'too_long' }, 400);
  }

  const rawKind = String(body.kind || 'bug').toLowerCase();
  const kind = rawKind === 'feature' ? 'feature' : rawKind === 'other' ? 'other' : 'bug';

  const rawSource = String(body.source || 'web').toLowerCase();
  const source = rawSource === 'app' ? 'app' : 'web';

  const rawPlatform = String(body.platform || 'other').toLowerCase();
  const platform = rawPlatform === 'win' ? 'win' : rawPlatform === 'mac' ? 'mac' : 'other';

  const appVersion = String(body.version || (source === 'web' ? 'web' : '')).trim().slice(0, 32);
  const lang = typeof body.lang === 'string' && /^[a-z]{2}$/i.test(body.lang) ? body.lang.toLowerCase() : 'en';
  const contact = String(body.contact || '').trim().slice(0, 120);
  const meta = String(body.meta || '').trim().slice(0, 400);
  const country = ((request.cf && request.cf.country) || 'XX').slice(0, 4).toUpperCase();
  const createdAt = new Date().toISOString();

  if (!env.STATS_DB) {
    return json({ ok: false, error: 'db_unavailable' }, 500);
  }

  try {
    await env.STATS_DB.prepare(
      `INSERT INTO feedback (created_at, source, kind, platform, app_version, lang, country, contact, message, meta)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)`
    )
      .bind(createdAt, source, kind, platform, appVersion, lang, country, contact, message, meta)
      .run();
    return json({ ok: true }, 200);
  } catch {
    return json({ ok: false, error: 'db_error' }, 500);
  }
}
