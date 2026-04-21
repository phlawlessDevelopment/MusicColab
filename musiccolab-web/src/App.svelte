<script lang="ts">
  import { fade, fly } from 'svelte/transition';
  import { onMount } from 'svelte';
  import { api, API_BASE } from './lib/api';
  import type { ArtistCard, CompareResponse, Preference, UserSummary } from './lib/types';

  type View = 'swipe' | 'compare';

  let email = $state('alice@example.com');
  let password = $state('Password123!');
  let token = $state('');
  let currentUserId = $state('');
  let currentEmail = $state('');
  let statusMessage = $state('');
  let busy = $state(false);

  let activeView = $state<View>('swipe');

  let currentArtist = $state<ArtistCard | null>(null);
  let artistQueue = $state<ArtistCard[]>([]);
  let dragStartX = 0;
  let dragStartY = 0;
  let isDragging = $state(false);
  let activePointerId: number | null = null;
  let dragOffsetX = $state(0);

  let users = $state<UserSummary[]>([]);
  let compareTarget = $state('');
  let compareResult = $state<CompareResponse | null>(null);

  const FALLBACK_ARTIST_IMAGE =
    'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 675"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0%" stop-color="%23122a38"/><stop offset="100%" stop-color="%23274758"/></linearGradient></defs><rect width="1200" height="675" fill="url(%23g)"/><circle cx="340" cy="220" r="160" fill="rgba(255,255,255,0.08)"/><circle cx="920" cy="460" r="210" fill="rgba(255,255,255,0.06)"/><text x="50%" y="52%" text-anchor="middle" fill="%23f6f8fb" font-size="56" font-family="Arial, sans-serif">Artist Image Unavailable</text></svg>';

  const hasSession = $derived(Boolean(token && currentUserId));
  const FEED_BATCH_SIZE = 5;
  const swipeDirection = $derived(dragOffsetX > 0 ? 'Like' : dragOffsetX < 0 ? 'Nope' : '');
  const swipeProgress = $derived(Math.min(1, Math.abs(dragOffsetX) / 120));
  const visibleGenres = $derived(currentArtist ? currentArtist.genres.slice(0, 4) : []);
  const hiddenGenreCount = $derived(currentArtist ? Math.max(currentArtist.genres.length - 4, 0) : 0);

  onMount(async () => {
    const savedToken = localStorage.getItem('musiccolab_token');
    const savedUserId = localStorage.getItem('musiccolab_user_id');
    const savedEmail = localStorage.getItem('musiccolab_email');
    if (savedToken && savedUserId && savedEmail) {
      token = savedToken;
      currentUserId = savedUserId;
      currentEmail = savedEmail;
      try {
        await Promise.all([loadNextArtist(), loadUsers()]);
      } catch (error) {
        recoverFromInvalidSession(error);
      }
    }
  });

  async function register() {
    await withBusy(async () => {
      const auth = await api.register(email, password);
      setSession(auth.token, auth.userId, auth.email);
      statusMessage = `Welcome, ${auth.email}. Your taste profile is ready.`;
      await Promise.all([loadNextArtist(), loadUsers()]);
    });
  }

  async function login() {
    await withBusy(async () => {
      const auth = await api.login(email, password);
      setSession(auth.token, auth.userId, auth.email);
      statusMessage = 'Signed in successfully.';
      await Promise.all([loadNextArtist(), loadUsers()]);
    });
  }

  function logout() {
    token = '';
    currentUserId = '';
    currentEmail = '';
    currentArtist = null;
    artistQueue = [];
    compareResult = null;
    localStorage.clear();
  }

  function setSession(nextToken: string, userId: string, userEmail: string) {
    token = nextToken;
    currentUserId = userId;
    currentEmail = userEmail;
    artistQueue = [];
    currentArtist = null;
    localStorage.setItem('musiccolab_token', nextToken);
    localStorage.setItem('musiccolab_user_id', userId);
    localStorage.setItem('musiccolab_email', userEmail);
  }

  async function loadNextArtist() {
    if (!token) return;

    if (artistQueue.length === 0) {
      const result = await api.getFeed(token, FEED_BATCH_SIZE);
      artistQueue = result.artists;
    }

    currentArtist = artistQueue[0] ?? null;
    artistQueue = artistQueue.slice(1);
  }

  async function loadUsers() {
    users = await api.getUsers();
    if (!compareTarget) {
      compareTarget = users.find((u) => u.id !== currentUserId)?.id ?? '';
    }
  }

  async function savePreference(preference: Preference) {
    if (!token || !currentArtist) return;
    await withBusy(async () => {
      await api.savePreference(token, currentArtist, preference);
      resetDrag();
      await loadNextArtist();
    });
  }

  async function skipArtist() {
    await withBusy(async () => {
      resetDrag();
      await loadNextArtist();
    });
  }

  async function compareUsers() {
    if (!currentUserId || !compareTarget) return;
    await withBusy(async () => {
      compareResult = await api.compare(currentUserId, compareTarget);
      statusMessage = 'Compatibility refreshed.';
    });
  }

  async function withBusy(work: () => Promise<void>) {
    busy = true;
    try {
      statusMessage = '';
      await work();
    } catch (error) {
      if (!recoverFromInvalidSession(error)) {
        statusMessage = error instanceof Error ? error.message : 'Unexpected error';
      }
    } finally {
      busy = false;
    }
  }

  function recoverFromInvalidSession(error: unknown) {
    const message = error instanceof Error ? error.message : 'Unexpected error';
    if (!message.toLowerCase().includes('sign in again')) {
      return false;
    }

    logout();
    statusMessage = 'Your session was reset with the database. Sign in again.';
    return true;
  }

  function handlePointerDown(event: PointerEvent) {
    if (!currentArtist || busy) return;
    activePointerId = event.pointerId;
    dragStartX = event.clientX;
    dragStartY = event.clientY;
    isDragging = true;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  }

  async function handlePointerUp(event: PointerEvent) {
    if (activePointerId !== event.pointerId) return;

    const delta = event.clientX - dragStartX;
    const deltaY = event.clientY - dragStartY;
    if (delta > 110) {
      await savePreference('Like');
    } else if (delta < -110) {
      await savePreference('Dislike');
    } else if (deltaY < -130) {
      await skipArtist();
    }

    resetDrag();

    if ((event.currentTarget as HTMLElement).hasPointerCapture(event.pointerId)) {
      (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
    }
  }

  function handlePointerMove(event: PointerEvent) {
    if (!isDragging || activePointerId !== event.pointerId) return;
    dragOffsetX = event.clientX - dragStartX;
  }

  function handlePointerCancel(event: PointerEvent) {
    if (activePointerId !== event.pointerId) return;
    resetDrag();
  }

  function resetDrag() {
    dragStartX = 0;
    dragStartY = 0;
    dragOffsetX = 0;
    isDragging = false;
    activePointerId = null;
  }

  function stopCardPointerPropagation(event: Event) {
    event.stopPropagation();
  }
</script>

<div class="shell">
  <header class="topbar">
    <div class="brand-block">
      <p class="eyebrow">MusicColab Studio</p>
      <h1>Music taste intelligence for collaborative listening</h1>
      <p class="subhead">Rate artists, compare overlap, and turn music chemistry into shareable insight.</p>
    </div>
    <div class="topbar-meta">
      <div class="api-chip">API {API_BASE}</div>
      {#if hasSession}
        <div class="session-chip">
          <div>
            <p class="chip-label">Signed in</p>
            <strong>{currentEmail}</strong>
          </div>
          <button class="ghost" onclick={logout}>Sign out</button>
        </div>
      {/if}
    </div>
  </header>

  {#if !hasSession}
    <section class="auth" in:fade>
      <div class="auth-intro">
        <h2>Open your listening workspace</h2>
        <p>
          Sign in to continue rating artists and comparing listening DNA with other users.
        </p>
      </div>
      <div class="auth-grid">
        <label>Email<input bind:value={email} type="email" autocomplete="email" /></label>
        <label>
          Password
          <input bind:value={password} type="password" autocomplete="current-password" />
        </label>
      </div>
      <div class="actions">
        <button onclick={login} disabled={busy}>Sign in</button>
        <button class="secondary" onclick={register} disabled={busy}>Create account</button>
      </div>
      <div class="auth-footnotes">
        <p>Seed users are available for demo: alice@example.com and bob@example.com</p>
        <p>Use secure passwords and your own account for production environments.</p>
      </div>
      {#if statusMessage}
        <p class="message" aria-live="polite">{statusMessage}</p>
      {/if}
    </section>
  {:else}
    <main class="workspace">
      <aside class="workspace-nav">
        <nav class="view-switcher" aria-label="Views">
          <button class:active={activeView === 'swipe'} onclick={() => (activeView = 'swipe')}>
            Rating Deck
          </button>
          <button class:active={activeView === 'compare'} onclick={() => (activeView = 'compare')}>
            Compatibility
          </button>
        </nav>

        <section class="panel note-panel">
          <h3>Workflow</h3>
          <p>1. Rate artists quickly in the deck.</p>
          <p>2. Compare profiles against other listeners.</p>
          <p>3. Use overlap and conflict data to plan shared sessions.</p>
        </section>

        <section class="panel note-panel">
          <h3>Interaction map</h3>
          <p>Swipe right: Like</p>
          <p>Swipe left: Dislike</p>
          <p>Swipe up: Skip</p>
        </section>
      </aside>

      <section class="workspace-content">
        {#if activeView === 'swipe'}
          <section class="screen swipe-screen" in:fly={{ y: 14, duration: 220 }}>
            <div class="screen-head">
              <div>
                <h2>Rating deck</h2>
                <p>Classify artists in seconds to sharpen your recommendation signal.</p>
              </div>
            </div>

            {#if currentArtist}
              <div class="swipe-stage">
                <article
                  class="artist-card active-card"
                  class:dragging={isDragging}
                  style={`transform: translateX(${dragOffsetX}px) rotate(${dragOffsetX / 26}deg)`}
                  onpointerdown={handlePointerDown}
                  onpointermove={handlePointerMove}
                  onpointerup={handlePointerUp}
                  onpointercancel={handlePointerCancel}
                >
                  <div class="swipe-badge like" style={`opacity:${swipeDirection === 'Like' ? swipeProgress : 0}`}>
                    Like
                  </div>
                  <div class="swipe-badge nope" style={`opacity:${swipeDirection === 'Nope' ? swipeProgress : 0}`}>
                    Nope
                  </div>
                  <img src={currentArtist.imageUrl ?? FALLBACK_ARTIST_IMAGE} alt={currentArtist.name} />
                  <div class="card-content">
                    <h3>{currentArtist.name}</h3>
                    {#if visibleGenres.length}
                      <div class="genre-chips" aria-label="Artist genres">
                        {#each visibleGenres as genre}
                          <span class="genre-chip">{genre}</span>
                        {/each}
                        {#if hiddenGenreCount > 0}
                          <span class="genre-chip genre-chip-more">+{hiddenGenreCount}</span>
                        {/if}
                      </div>
                    {:else}
                      <p>Genre data pending</p>
                    {/if}

                    {#if currentArtist.previewUrl}
                      <div class="audio-preview">
                        <audio
                          controls
                          preload="none"
                          src={currentArtist.previewUrl}
                          aria-label="Artist audio preview"
                          onpointerdown={stopCardPointerPropagation}
                          onpointermove={stopCardPointerPropagation}
                          onpointerup={stopCardPointerPropagation}
                          onpointercancel={stopCardPointerPropagation}
                          onclick={stopCardPointerPropagation}
                        ></audio>
                      </div>
                    {/if}
                  </div>
                </article>

                <div class="swipe-hint">
                  <span>Left: pass</span>
                  <span>Right: like</span>
                  <span>Up: skip</span>
                </div>
              </div>

              <div class="actions">
                <button class="danger" onclick={() => savePreference('Dislike')} disabled={busy}>Dislike</button>
                <button class="secondary" onclick={skipArtist} disabled={busy}>Skip</button>
                <button class="success" onclick={() => savePreference('Like')} disabled={busy}>Like</button>
              </div>
            {:else}
              <p>No artist available right now. The app will keep looking for the next one.</p>
            {/if}
          </section>
        {:else}
          <section class="screen compare-screen" in:fly={{ y: 14, duration: 220 }}>
            <div class="screen-head">
              <div>
                <h2>Compatibility lab</h2>
                <p>Measure overlap, surface disagreement, and identify discovery potential.</p>
              </div>
              <button class="secondary" onclick={loadUsers} disabled={busy}>Reload users</button>
            </div>

            <div class="compare-controls">
              <label>
                Compare with
                <select bind:value={compareTarget}>
                  {#each users as user}
                    {#if user.id !== currentUserId}
                      <option value={user.id}>{user.email}</option>
                    {/if}
                  {/each}
                </select>
              </label>
              <button onclick={compareUsers} disabled={busy || !compareTarget}>Run comparison</button>
            </div>

            {#if compareResult}
              <div class="score-tile">
                <span>Compatibility score</span>
                <strong>{compareResult.compatibilityScore}</strong>
              </div>

              <div class="insights-grid">
                <section>
                  <h3>Shared likes</h3>
                  {#each compareResult.sharedLikes as artist}
                    <p>{artist.name}</p>
                  {:else}
                    <p>No overlap yet.</p>
                  {/each}
                </section>

                <section>
                  <h3>Conflicts</h3>
                  {#each compareResult.conflicts as artist}
                    <p>{artist.name}</p>
                  {:else}
                    <p>No conflicts so far.</p>
                  {/each}
                </section>

                <section>
                  <h3>Discovery from you</h3>
                  {#each compareResult.discoveryFromA as artist}
                    <p>{artist.name}</p>
                  {:else}
                    <p>No new discoveries yet.</p>
                  {/each}
                </section>

                <section>
                  <h3>Discovery from them</h3>
                  {#each compareResult.discoveryFromB as artist}
                    <p>{artist.name}</p>
                  {:else}
                    <p>No new discoveries yet.</p>
                  {/each}
                </section>
              </div>
            {/if}
          </section>
        {/if}
      </section>
    </main>

    {#if statusMessage}
      <p class="message" aria-live="polite">{statusMessage}</p>
    {/if}
  {/if}
</div>
