import { GitHubIssueWithStatus, Message, MessageFragment } from '../types/ChatTypes';
import { UnboundedChannel } from '../utils/UnboundedChannel';

export interface GitHubIssue {
    owner: string;
    repository: string;
    issueNumber: number;
}

class ChatService {
    private static instance: ChatService;
    private backendUrl: string;
    private activeStreams = new Map<string, { eventSource: EventSource, channel: UnboundedChannel<MessageFragment> }>();

    private constructor(backendUrl: string) {
        this.backendUrl = backendUrl;
    }

    static getInstance(backendUrl: string): ChatService {
        if (!ChatService.instance) {
            ChatService.instance = new ChatService(backendUrl);
        }
        return ChatService.instance;
    }

    private getIssueId(issue: GitHubIssue): string {
        return `${issue.owner}/${issue.repository}/${issue.issueNumber}`;
    }

    async getActiveIssues(): Promise<GitHubIssueWithStatus[]> {
        const response = await fetch(`${this.backendUrl}`);
        if (!response.ok) {
            throw new Error('Error fetching active issues');
        }
        return await response.json();
    }

    async *stream(
        issue: GitHubIssue,
        messageCount: number,
        abortController: AbortController
    ): AsyncGenerator<MessageFragment> {
        const issueId = this.getIssueId(issue);
        let startIndex = messageCount;

        // Set up and store the abort event handler
        const abortHandler = () => {
            console.log(`Aborting stream for issue: ${issueId}`);
            const activeStream = this.activeStreams.get(issueId);
            if (activeStream) {
                activeStream.eventSource.close();
                activeStream.channel.close();
                this.activeStreams.delete(issueId);
            }
        };

        abortController.signal.addEventListener('abort', abortHandler);

        try {
            while (!abortController.signal.aborted) {
                let channel = new UnboundedChannel<MessageFragment>();
                
                try {
                    const eventSource = new EventSource(`${this.backendUrl}/i/chat/${issueId}/stream?startIndex=${startIndex}`);
                    
                    // Store the event source and channel
                    this.activeStreams.set(issueId, { eventSource, channel });
                    
                    // Handle messages
                    eventSource.addEventListener('message', (event) => {
                        try {
                            const value = JSON.parse(event.data);
                            const fragment: MessageFragment = {
                                role: value.role,
                                type: value.type,
                                text: value.text,
                                isFinal: value.isFinal ?? false,
                                responseId: value.responseId,
                                data: value.data
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
                        console.debug(`Stream completed for issue: ${issueId}`);
                        eventSource.close();
                        channel.close();
                    });
                    
                    // Handle error event
                    eventSource.addEventListener('error', event => {
                        console.error(`Stream error for issue: ${issueId}: ${event}`);
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
                    // Only break the loop if we're aborting
                    if (abortController.signal.aborted) {
                        break;
                    }
                } finally {
                    this.activeStreams.delete(issueId);
                }

                // If we're not aborting, wait a second before retrying
                if (!abortController.signal.aborted) {
                    console.debug(`Retrying stream for issue: ${issueId} in 1 second`);
                    await new Promise(resolve => setTimeout(resolve, 1000));
                }
            }
        } finally {
            abortController.signal.removeEventListener('abort', abortHandler);
        }
    }

    async sendPrompt(issue: GitHubIssue, prompt: string): Promise<void> {
        const issueId = this.getIssueId(issue);
        const response = await fetch(`${this.backendUrl}/i/chat/${issueId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: prompt })
        });

        if (!response.ok) {
            let errorMessage;
            try {
                errorMessage = await response.text();
            } catch (e) {
                errorMessage = response.statusText;
            }
            throw new Error(`Error sending prompt: ${errorMessage}`);
        }
    }

    async deleteIssueChat(issue: GitHubIssue): Promise<void> {
        const issueId = this.getIssueId(issue);
        const response = await fetch(`${this.backendUrl}/i/${issueId}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            throw new Error('Failed to delete issue chat');
        }
    }

    async cancelIssueChat(issue: GitHubIssue): Promise<void> {
        const issueId = this.getIssueId(issue);
        const response = await fetch(`${this.backendUrl}/i/${issueId}/cancel`, {
            method: 'POST'
        });
        if (!response.ok) {
            throw new Error('Failed to cancel issue chat');
        }
    }
}

export default ChatService;
