export type Preference = 'Like' | 'Dislike';

export interface AuthResponse {
    token: string;
    userId: string;
    email: string;
}

export interface ArtistCard {
    id: string;
    name: string;
    imageUrl: string | null;
    previewUrl: string | null;
    genres: string[];
}

export interface ArtistFeedResponse {
    artists: ArtistCard[];
}

export interface UserSummary {
    id: string;
    email: string;
    createdAt: string;
}

export interface ProfileResponse {
    user: UserSummary;
    likes: number;
    dislikes: number;
    totalRatings: number;
}

export interface CompareResponse {
    compatibilityScore: number;
    sharedLikes: ArtistCard[];
    conflicts: ArtistCard[];
    discoveryFromA: ArtistCard[];
    discoveryFromB: ArtistCard[];
}
