const BOT_RE = /bot|crawl|spider|slurp|headless|curl|wget|python|uptime|pingdom|lighthouse|bytespider|gptbot|claudebot|facebookexternalhit|twitterbot|discordbot|telegrambot|whatsapp/i;

function normalizeRef(raw) {
  if (!raw || typeof raw !== 'string') return 'direct';
  try {
    const host = new URL(raw).hostname.toLowerCase().replace(/^www\./, '');
    if (!host || host === 'squadeditor.pages.dev') return 'direct';
    if (host === 't.co' || host === 'twitter.com' || host === 'x.com') return 'x.com';
    if (host.startsWith('google.') || host.endsWith('.google.com')) return 'google';
    if (host.endsWith('reddit.com') || host === 'out.reddit.com') return 'reddit.com';
    if (host.endsWith('sortitoutsi.net')) return 'sortitoutsi.net';
    if (host.endsWith('github.com')) return 'github.com';
    return host.slice(0, 64);
  } catch {
    return 'direct';
  }
}

export async function onRequestPost(context) {
  const { request, env } = context;
  const ua = request.headers.get('user-agent') || '';
  if (!ua || BOT_RE.test(ua)) {
    return new Response(null, { status: 204 });
  }

  let body = {};
  try {
    body = await request.json();
  } catch {
    return new Response(null, { status: 204 });
  }

  const event = body.e === 'dl' ? 'dl' : body.e === 'gh' ? 'gh' : 'view';
  const ref = normalizeRef(body.r);
  const country = ((request.cf && request.cf.country) || 'XX').slice(0, 4).toUpperCase();
  const lang = (typeof body.l === 'string' && /^[a-z]{2}$/i.test(body.l) ? body.l.toLowerCase() : 'en');
  const day = new Date().toISOString().slice(0, 10);

  if (env.STATS_DB) {
    try {
      await env.STATS_DB.prepare(
        `INSERT INTO daily_stats (day, event, ref, country, lang, hits)
         VALUES (?1, ?2, ?3, ?4, ?5, 1)
         ON CONFLICT(day, event, ref, country, lang)
         DO UPDATE SET hits = hits + 1`
      ).bind(day, event, ref, country, lang).run();
    } catch {
      // Never break client UX on analytics failure
    }
  }

  return new Response(null, { status: 204 });
}
