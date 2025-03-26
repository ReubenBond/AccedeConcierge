export interface Message {
    role: string;
    text: string;
    responseId: string;
    type: string;
    data?: string;
    attachments?: FileAttachment[];
}

export interface FileAttachment {
    name: string;
    url: string;
    type: string;
}

export interface MessageFragment extends Message {
    isFinal: boolean;
}
