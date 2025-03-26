export interface Message {
    role: string;
    text: string;
    responseId: string;
    type: string;
    attachments?: FileAttachment[];
}

export interface FileAttachment {
    uri: string;
    contentType: string;
}

export interface MessageFragment extends Message {
    isFinal: boolean;
}
