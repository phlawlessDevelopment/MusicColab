import type {
    ArtistFeedResponse,
    AuthResponse,
    CompareResponse,
    Preference,
    ProfileResponse,
    UserSummary
} from './types';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5150';

async function request<T>(path: string, init?: RequestInit): Promise<T> {
    const response = await fetch(`${API_BASE}${path}`, {
        ...init,
        headers: {
            'Content-Type': 'application/json',
            ...(init?.headers ?? {})
        }
    });

    if (!response.ok) {
        const text = await response.text();
        let message = text;

        if (text.trim()) {
            try {
                const payload = JSON.parse(text) as { message?: string };
                message = payload.message ?? text;
            } catch {
                message = text;
            }
        }

        if (response.status === 401) {
            throw new Error(message || 'Your session expired. Sign in again.');
        }

        throw new Error(message || `Request failed (${response.status})`);
    }

    if (response.status === 204) {
        return undefined as T;
    }

    const body = await response.text();
    if (!body.trim()) {
        return undefined as T;
    }

    try {
        return JSON.parse(body) as T;
    } catch {
        throw new Error(`Received invalid JSON from ${path}`);
    }
}

export const api = {
    register(email: string, password: string) {
        return request<AuthResponse>('/auth/register', {
            method: 'POST',
            body: JSON.stringify({ email, password })
        });
    },

    login(email: string, password: string) {
        return request<AuthResponse>('/auth/login', {
            method: 'POST',
            body: JSON.stringify({ email, password })
        });
    },

    getFeed(token: string, limit = 12) {
        return request<ArtistFeedResponse>(`/artists/feed?limit=${limit}`, {
            headers: { Authorization: `Bearer ${token}` }
        });
    },

    savePreference(token: string, artistId: string, preference: Preference) {
        return request<void>('/preferences', {
            method: 'POST',
            headers: { Authorization: `Bearer ${token}` },
            body: JSON.stringify({ artistId, preference })
        });
    },

    getUsers() {
        return request<UserSummary[]>('/users');
    },

    getProfile(userId: string) {
        return request<ProfileResponse>(`/users/${userId}/profile`);
    },

    compare(userA: string, userB: string) {
        return request<CompareResponse>(`/compare/${userA}/${userB}`);
    }
};

export { API_BASE };
