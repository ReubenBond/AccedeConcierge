import { Message, MessageFragment } from '../types/ChatTypes';
import { UnboundedChannel } from '../utils/UnboundedChannel';

class ChatService {
    private static instance: ChatService;
    private backendUrl: string;
    private activeStream?: { eventSource: EventSource, channel: UnboundedChannel<MessageFragment> };

    private constructor(backendUrl: string) {
        this.backendUrl = backendUrl;
    }

    static getInstance(backendUrl: string): ChatService {
        if (!ChatService.instance) {
            ChatService.instance = new ChatService(backendUrl);
        }
        return ChatService.instance;
    }

    async *stream(
        startIndex: number,
        abortController: AbortController
    ): AsyncGenerator<MessageFragment> {
        const abortHandler = () => {
            console.log('Aborting chat stream');
            if (this.activeStream) {
                this.activeStream.eventSource.close();
                this.activeStream.channel.close();
                this.activeStream = undefined;
            }
        };

        abortController.signal.addEventListener('abort', abortHandler);

        try {
            while (!abortController.signal.aborted) {
                let channel = new UnboundedChannel<MessageFragment>();
                
                try {
                    const eventSource = new EventSource(`${this.backendUrl}/chat/stream?startIndex=${startIndex}`);
                    
                    // Store the event source and channel
                    this.activeStream = { eventSource, channel };
                    
                    // Handle messages
                    eventSource.addEventListener('message', (event) => {
                        try {
                            const chatItem = JSON.parse(event.data);
                            
                            // Create a MessageFragment from the ChatItem
                            const fragment: MessageFragment = {
                                role: chatItem.role,
                                type: chatItem.type,
                                text: chatItem.text,
                                responseId: chatItem.responseId,
                                isFinal: chatItem.isFinal ?? false,
                                attachments: chatItem.attachments
                            };

                            if (fragment.isFinal) {
                                // Increment the start index for the next connection
                                startIndex++;
                            }
                            
                            channel.write(fragment);
                        } catch (error) {
                            console.error(`Error processing SSE message: ${error}`);
                        }
                    });
                    
                    // Handle complete event
                    eventSource.addEventListener('complete', () => {
                        console.debug('Stream completed');
                        eventSource.close();
                        channel.close();
                    });
                    
                    // Handle error event
                    eventSource.addEventListener('error', event => {
                        console.error(`Stream error: ${event}`);
                        eventSource.close();
                        channel.throwError(new Error('SSE connection failed'));
                    });

                    try {
                        for await (const fragment of channel) {
                            yield fragment;
                        }
                    } finally {
                        eventSource.close();
                    }
                } catch (error) {
                    console.error('Stream error:', error);
                    if (abortController.signal.aborted) {
                        break;
                    }
                } finally {
                    this.activeStream = undefined;
                }

                if (!abortController.signal.aborted) {
                    console.debug('Retrying stream in 1 second');
                    await new Promise(resolve => setTimeout(resolve, 1000));
                }
            }
        } finally {
            abortController.signal.removeEventListener('abort', abortHandler);
        }
    }

    async sendMessage(text: string, files?: File[]): Promise<void> {
        files = files || [];
        const formData = new FormData();
        formData.append('Text', text);
        
        // Append each file to the form data
        files.forEach(file => {
            formData.append('file', file);
        });
        
        const response = await fetch(`${this.backendUrl}/chat/messages`, {
            method: 'POST',
            body: formData
        });

        if (!response.ok) {
            let errorMessage;
            try {
                errorMessage = await response.text();
            } catch (e) {
                errorMessage = response.statusText;
            }
            throw new Error(`Error sending message with attachments: ${errorMessage}`);
        }
    }

    async cancelChat(): Promise<void> {
        const response = await fetch(`${this.backendUrl}/chat/stream/cancel`, {
            method: 'POST'
        });
        if (!response.ok) {
            throw new Error('Failed to cancel chat');
        }
    }
}

export default ChatService;
