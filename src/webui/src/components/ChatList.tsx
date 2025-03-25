import { useNavigate, useParams } from 'react-router-dom';
import { Chat } from '../types/ChatTypes';

interface ChatListProps {
  chats: Chat[];
  selectedChat: Chat | null;
  setSelectedChat: (chat: Chat) => void;
}

function ChatList({ chats, selectedChat, setSelectedChat }: ChatListProps) {
    const navigate = useNavigate();
    const params = useParams();

    return (
        <div>
            {
                chats.map(chat => (
                    <button
                        key={chat.id}
                        onClick={() => {
                            setSelectedChat(chat);
                            if (chat.id !== params.chatId) {
                                navigate(`/chat/${chat.id}`);
                            }
                        }}
                    >
                        {chat.issue.title}
                    </button>
                ))
            }
        </div>
    );
}

export default ChatList;
