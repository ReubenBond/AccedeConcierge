export interface Message {
    role: string;
    text: string;
    responseId: string;
    type: string;
    data?: string;
}

export interface MessageFragment extends Message {
    isFinal: boolean;
}
